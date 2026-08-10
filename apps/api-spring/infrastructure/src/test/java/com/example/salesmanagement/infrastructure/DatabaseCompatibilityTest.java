package com.example.salesmanagement.infrastructure;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.example.salesmanagement.application.SalesCaseStore;
import com.example.salesmanagement.application.SaveResult;
import com.example.salesmanagement.domain.Count;
import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.ItemCategory;
import com.example.salesmanagement.domain.LotCommon;
import com.example.salesmanagement.domain.LotDetail;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.ManufacturedLot;
import com.example.salesmanagement.domain.ManufacturingLot;
import com.example.salesmanagement.domain.NonEmptyList;
import com.example.salesmanagement.domain.Quantity;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.math.BigDecimal;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.LocalDate;
import java.time.ZoneOffset;
import java.util.List;
import java.util.Optional;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.testcontainers.postgresql.PostgreSQLContainer;

final class DatabaseCompatibilityTest {
    private static final PostgreSQLContainer DATABASE = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");
    private static JdbcTemplate jdbc;
    private static SharedMigrationRunner migrations;
    private static DataSourceTransactionManager transactionManager;

    @BeforeAll
    static void migrate() {
        DATABASE.start();
        var dataSource =
                new DriverManagerDataSource(DATABASE.getJdbcUrl(), DATABASE.getUsername(), DATABASE.getPassword());
        transactionManager = new DataSourceTransactionManager(dataSource);
        jdbc = new JdbcTemplate(dataSource);
        migrations = new SharedMigrationRunner(dataSource, transactionManager);
        migrations.migrate();
    }

    @AfterAll
    static void stopDatabase() {
        DATABASE.close();
    }

    @Test
    void allMigrationsAreIdempotentAndMatchFsharpJournalOrder() throws Exception {
        migrations.migrate();
        var actual = jdbc.query(
                "SELECT scriptname FROM schemaversions ORDER BY schemaversionsid", (row, index) -> row.getString(1));
        var expected = Files.readAllLines(Path.of(System.getProperty("repository.root"))
                .resolve("apps/api-spring/infrastructure/src/main/resources/db/migration-order.txt"));
        assertEquals(expected, actual);
    }

    @Test
    void actualSchemaExactlyMatchesFsharpSnapshot() throws Exception {
        var root = Path.of(System.getProperty("repository.root"));
        String snapshotQuery = Files.readString(root.resolve("apps/api-fsharp/scripts/schema-snapshot.sql"));
        var actual = jdbc.query(snapshotQuery, (row, index) -> row.getString(1));
        var expected = Files.readAllLines(root.resolve("apps/api-fsharp/schema-snapshot.txt"));
        assertEquals(expected, actual);
    }

    @Test
    void codeMastersMatchSeededHierarchyAndResolveNames() {
        var masters = new JdbcCodeMasterQueries(jdbc).loadAll();

        assertTrue(masters.divisions().size() >= 2);
        assertEquals(1, masters.departments().getFirst().divisionCode());
        assertEquals(10, masters.sections().getFirst().departmentCode());
        assertEquals(Optional.of("第一事業部"), masters.divisionName(1));
        assertEquals(Optional.empty(), masters.divisionName(999));
    }

    @Test
    void repositoryRoundTripOptimisticLockAndOutboxShareOneTransaction() {
        var repository = new JdbcLotRepository(
                jdbc, transactionManager, new ObjectMapper().registerModule(new JavaTimeModule()));
        var number = new LotNumber(2099, "TC", 1);
        var manufacturing = new ManufacturingLot(common(number));

        assertInstanceOf(SaveResult.Saved.class, repository.insert(manufacturing, "test"));
        assertEquals(manufacturing, repository.find(number).orElseThrow().value());

        var date = LocalDate.parse("2099-01-02");
        var manufactured = new ManufacturedLot(manufacturing.common(), date);
        var event = new DomainEvent.LotManufacturingCompleted(number, date);
        var saved =
                assertInstanceOf(SaveResult.Saved.class, repository.update(manufactured, 1, "test", List.of(event)));
        assertEquals(2, saved.lot().version());
        assertEquals(
                1,
                jdbc.queryForObject(
                        "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'LotManufacturingCompleted'",
                        Integer.class));
        assertEquals(
                number.toString(),
                jdbc.queryForObject(
                        "SELECT payload->>'lotId' FROM outbox_events WHERE event_type = 'LotManufacturingCompleted'",
                        String.class));
        assertEquals(
                date.toString(),
                jdbc.queryForObject(
                        "SELECT payload->>'date' FROM outbox_events WHERE event_type = 'LotManufacturingCompleted'",
                        String.class));

        assertInstanceOf(SaveResult.Conflict.class, repository.update(manufactured, 1, "test", List.of(event)));
        assertEquals(
                1,
                jdbc.queryForObject(
                        "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'LotManufacturingCompleted'",
                        Integer.class));
    }

    @Test
    void salesCaseSubtypePayloadsRoundTripAndRollbackOnStaleVersion() {
        var lotRepository = new JdbcLotRepository(
                jdbc, transactionManager, new ObjectMapper().registerModule(new JavaTimeModule()));
        var lotNumber = new LotNumber(2098, "CASE", 1);
        var manufactured = new ManufacturedLot(common(lotNumber), LocalDate.parse("2098-01-10"));
        assertInstanceOf(SaveResult.Saved.class, lotRepository.insert(manufactured, "test"));

        var store = new JdbcSalesCaseStore(jdbc, transactionManager);
        var salesDate = LocalDate.parse("2098-01-15");
        var direct = store.create("direct", 1, salesDate, List.of(lotNumber));
        var appraisal = new SalesCaseStore.DirectAppraisal(
                "normal",
                LocalDate.parse("2098-01-20"),
                LocalDate.parse("2098-01-25"),
                "market",
                LocalDate.parse("2098-01-01"),
                LocalDate.parse("2098-01-01"),
                LocalDate.parse("2098-01-01"),
                100_000,
                Optional.empty(),
                Optional.empty(),
                List.of(new SalesCaseStore.LotAppraisal(
                        lotNumber,
                        List.of(new SalesCaseStore.DetailAppraisal(
                                1, 1_000, BigDecimal.ONE, BigDecimal.ONE, Optional.empty())))));
        var appraised = store.saveAppraisal(direct.number(), 1, "before_appraisal", appraisal)
                .orElseThrow();
        assertEquals(
                appraisal.appraisalDate(),
                ((SalesCaseStore.DirectDetails) appraised.details())
                        .appraisal()
                        .orElseThrow()
                        .appraisalDate());
        assertEquals(1, jdbc.queryForObject("SELECT COUNT(*) FROM lot_detail_appraisal", Integer.class));
        assertTrue(
                store.saveAppraisal(direct.number(), 1, "appraised", appraisal).isEmpty());

        var contract = new SalesCaseStore.DirectContract(
                LocalDate.parse("2098-02-01"),
                "person",
                "C001",
                Optional.of("agent"),
                1,
                "item",
                "delivery",
                Optional.empty(),
                1,
                Optional.empty(),
                Optional.empty(),
                100_000,
                10_000,
                100_000,
                10_000);
        var contracted = store.saveContract(direct.number(), 2, contract).orElseThrow();
        assertEquals(
                contract,
                ((SalesCaseStore.DirectDetails) contracted.details()).contract().orElseThrow());

        var reservation = store.create("reservation", 1, salesDate, List.of(lotNumber));
        var reserved = store.saveReservationPrice(
                        reservation.number(),
                        1,
                        new SalesCaseStore.ReservationPrice(
                                LocalDate.parse("2098-01-20"),
                                "reserved-info",
                                500_000,
                                Optional.empty(),
                                Optional.empty(),
                                Optional.empty()))
                .orElseThrow();
        var confirmed = store.confirmReservation(reservation.number(), 2, LocalDate.parse("2098-01-22"), 480_000)
                .orElseThrow();
        var delivered = store.deliverReservation(reservation.number(), 3, LocalDate.parse("2098-01-30"))
                .orElseThrow();
        var reservationDetails = (SalesCaseStore.ReservationDetails) delivered.details();
        assertEquals(
                "reserved-info",
                reservationDetails.reservationPrice().orElseThrow().reservedLotInfo());
        assertEquals(
                Optional.of(480_000),
                reservationDetails.reservationPrice().orElseThrow().determinedAmount());
        assertEquals(
                Optional.of(LocalDate.parse("2098-01-30")),
                reservationDetails.reservationPrice().orElseThrow().deliveredDate());
        assertEquals("reserved", reserved.status());
        assertEquals("reservation_confirmed", confirmed.status());

        var consignment = store.create("consignment", 1, salesDate, List.of(lotNumber));
        var designated = store.designateConsignment(
                        consignment.number(),
                        1,
                        new SalesCaseStore.Consignor("Acme", "C001", LocalDate.parse("2098-01-25")))
                .orElseThrow();
        var result = store.saveConsignmentResult(
                        consignment.number(),
                        2,
                        new SalesCaseStore.ConsignmentResult(LocalDate.parse("2098-01-30"), 480_000))
                .orElseThrow();
        assertEquals(
                "Acme",
                ((SalesCaseStore.ConsignmentDetails) designated.details())
                        .consignor()
                        .orElseThrow()
                        .name());
        assertEquals(
                480_000,
                ((SalesCaseStore.ConsignmentDetails) result.details())
                        .result()
                        .orElseThrow()
                        .resultAmount());
    }

    @Test
    void outboxProcessorClaimsPendingRowsAndReapsAbandonedClaims() {
        jdbc.update("TRUNCATE outbox_events RESTART IDENTITY");
        jdbc.update("INSERT INTO outbox_events(event_type, payload) VALUES ('A', '{}'::jsonb), ('B', '{}'::jsonb)");
        jdbc.update(
                """
                INSERT INTO outbox_events(event_type, payload, status, processed_at)
                VALUES ('C', '{}'::jsonb, 'processing', TIMESTAMPTZ '2026-08-08 23:00:00+00')
                """);
        var clock = Clock.fixed(Instant.parse("2026-08-09T00:00:00Z"), ZoneOffset.UTC);
        var processor = new JdbcOutboxProcessor(jdbc, transactionManager, clock, Duration.ofMinutes(5), 100);

        assertEquals(1, processor.resetAbandonedClaims());
        assertEquals(3, processor.processPending());
        assertEquals(
                3,
                jdbc.queryForObject(
                        "SELECT count(*) FROM outbox_events WHERE status = 'processed' AND processed_at IS NOT NULL",
                        Integer.class));
        assertEquals(0, processor.processPending());
    }

    private static LotCommon common(LotNumber number) {
        var detail = new LotDetail(
                ItemCategory.parse("general").orElseThrow(),
                Optional.empty(),
                "P-1",
                BigDecimal.ONE,
                BigDecimal.ONE,
                BigDecimal.TWO,
                "Q1",
                Count.create(1).value().orElseThrow(),
                Quantity.create(BigDecimal.ONE).value().orElseThrow(),
                Optional.empty());
        return new LotCommon(
                number, 1, 10, 100, 1, 1, 1, NonEmptyList.from(List.of(detail)).orElseThrow());
    }
}
