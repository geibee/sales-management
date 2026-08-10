package com.example.salesmanagement.infrastructure;

import com.example.salesmanagement.application.LotRepository;
import com.example.salesmanagement.application.SaveResult;
import com.example.salesmanagement.application.VersionedLot;
import com.example.salesmanagement.domain.ConversionInstructedLot;
import com.example.salesmanagement.domain.Count;
import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.InventoryLot;
import com.example.salesmanagement.domain.ItemCategory;
import com.example.salesmanagement.domain.LotCommon;
import com.example.salesmanagement.domain.LotDetail;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.ManufacturedLot;
import com.example.salesmanagement.domain.ManufacturingLot;
import com.example.salesmanagement.domain.NonEmptyList;
import com.example.salesmanagement.domain.Quantity;
import com.example.salesmanagement.domain.ShippedLot;
import com.example.salesmanagement.domain.ShippingInstructedLot;
import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.sql.Date;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.time.LocalDate;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

public final class JdbcLotRepository implements LotRepository {
    private final JdbcTemplate jdbc;
    private final TransactionTemplate transaction;
    private final ObjectMapper objectMapper;

    public JdbcLotRepository(
            JdbcTemplate jdbc, PlatformTransactionManager transactionManager, ObjectMapper objectMapper) {
        this.jdbc = jdbc;
        this.transaction = new TransactionTemplate(transactionManager);
        this.objectMapper = objectMapper;
    }

    @Override
    public Optional<VersionedLot> find(LotNumber number) {
        var headers = jdbc.query(
                """
                SELECT * FROM lot
                 WHERE lot_number_year = ? AND lot_number_location = ? AND lot_number_seq = ?
                """,
                (row, index) -> mapHeader(row),
                number.year(),
                number.location(),
                number.sequence());
        if (headers.isEmpty()) {
            return Optional.empty();
        }
        var header = headers.getFirst();
        var details = jdbc.query(
                """
                SELECT * FROM lot_detail
                 WHERE lot_number_year = ? AND lot_number_location = ? AND lot_number_seq = ?
                 ORDER BY seq_no
                """,
                (row, index) -> mapDetail(row),
                number.year(),
                number.location(),
                number.sequence());
        var common = new LotCommon(
                number,
                header.divisionCode(),
                header.departmentCode(),
                header.sectionCode(),
                header.processCategory(),
                header.inspectionCategory(),
                header.manufacturingCategory(),
                NonEmptyList.from(details).orElseThrow());
        return Optional.of(new VersionedLot(toLot(common, header), header.version()));
    }

    @Override
    public SaveResult insert(InventoryLot value, String actor) {
        try {
            return transaction.execute(status -> {
                var common = value.common();
                var dates = dates(value);
                jdbc.update(
                        """
                        INSERT INTO lot (
                            lot_number_year, lot_number_location, lot_number_seq,
                            division_code, department_code, section_code,
                            process_category, inspection_category, manufacturing_category,
                            status, manufacturing_completed_date, shipping_deadline_date,
                            shipped_date, destination_item, created_by, updated_by)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        common.lotNumber().year(),
                        common.lotNumber().location(),
                        common.lotNumber().sequence(),
                        common.divisionCode(),
                        common.departmentCode(),
                        common.sectionCode(),
                        common.processCategory(),
                        common.inspectionCategory(),
                        common.manufacturingCategory(),
                        value.status(),
                        dates.manufactured(),
                        dates.deadline(),
                        dates.shipped(),
                        dates.destination(),
                        actor,
                        actor);
                insertDetails(common);
                return SaveResult.saved(new VersionedLot(value, 1));
            });
        } catch (DuplicateKeyException exception) {
            return SaveResult.duplicate();
        }
    }

    @Override
    public SaveResult update(InventoryLot value, int expectedVersion, String actor, List<DomainEvent> events) {
        return transaction.execute(status -> {
            var common = value.common();
            var dates = dates(value);
            int changed = jdbc.update(
                    """
                    UPDATE lot SET status = ?, manufacturing_completed_date = ?,
                           shipping_deadline_date = ?, shipped_date = ?, destination_item = ?,
                           version = version + 1, updated_at = NOW(), updated_by = ?
                     WHERE lot_number_year = ? AND lot_number_location = ? AND lot_number_seq = ?
                       AND version = ?
                    """,
                    value.status(),
                    dates.manufactured(),
                    dates.deadline(),
                    dates.shipped(),
                    dates.destination(),
                    actor,
                    common.lotNumber().year(),
                    common.lotNumber().location(),
                    common.lotNumber().sequence(),
                    expectedVersion);
            if (changed == 0) {
                status.setRollbackOnly();
                return SaveResult.conflict();
            }
            events.forEach(this::insertOutbox);
            return SaveResult.saved(new VersionedLot(value, expectedVersion + 1));
        });
    }

    private void insertDetails(LotCommon common) {
        int sequence = 1;
        for (var detail : common.details().values()) {
            jdbc.update(
                    """
                    INSERT INTO lot_detail (
                        lot_number_year, lot_number_location, lot_number_seq, seq_no,
                        item_category, premium_category, product_category_code,
                        length_spec_lower, thickness_spec_lower, thickness_spec_upper,
                        quality_grade, quantity_count, quantity_amount, inspection_result_category)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    common.lotNumber().year(),
                    common.lotNumber().location(),
                    common.lotNumber().sequence(),
                    sequence,
                    detail.itemCategory().wireValue(),
                    detail.premiumCategory().orElse(null),
                    detail.productCategoryCode(),
                    detail.lengthSpecLower(),
                    detail.thicknessSpecLower(),
                    detail.thicknessSpecUpper(),
                    detail.qualityGrade(),
                    detail.count().value(),
                    detail.quantity().value(),
                    detail.inspectionResultCategory().orElse(null));
            sequence++;
        }
    }

    private void insertOutbox(DomainEvent event) {
        try {
            Object payload =
                    switch (event) {
                        case DomainEvent.LotManufacturingCompleted completed ->
                            Map.of(
                                    "lotId",
                                    completed.lotNumber().toString(),
                                    "date",
                                    completed.date().toString());
                    };
            jdbc.update(
                    "INSERT INTO outbox_events(event_type, payload) VALUES (?, ?::jsonb)",
                    event.eventType(),
                    objectMapper.writeValueAsString(payload));
        } catch (JsonProcessingException exception) {
            throw new IllegalStateException("Domain event serialization failed", exception);
        }
    }

    private static Header mapHeader(ResultSet row) throws SQLException {
        return new Header(
                row.getInt("division_code"),
                row.getInt("department_code"),
                row.getInt("section_code"),
                row.getInt("process_category"),
                row.getInt("inspection_category"),
                row.getInt("manufacturing_category"),
                row.getString("status"),
                nullableDate(row, "manufacturing_completed_date"),
                nullableDate(row, "shipping_deadline_date"),
                nullableDate(row, "shipped_date"),
                Optional.ofNullable(row.getString("destination_item")),
                row.getInt("version"));
    }

    private static LotDetail mapDetail(ResultSet row) throws SQLException {
        return new LotDetail(
                ItemCategory.parse(row.getString("item_category")).orElseThrow(),
                Optional.ofNullable(row.getString("premium_category")),
                row.getString("product_category_code"),
                row.getBigDecimal("length_spec_lower"),
                row.getBigDecimal("thickness_spec_lower"),
                row.getBigDecimal("thickness_spec_upper"),
                row.getString("quality_grade"),
                Count.create(row.getInt("quantity_count")).value().orElseThrow(),
                Quantity.create(row.getBigDecimal("quantity_amount")).value().orElseThrow(),
                Optional.ofNullable(row.getString("inspection_result_category")));
    }

    private static Optional<LocalDate> nullableDate(ResultSet row, String column) throws SQLException {
        Date value = row.getDate(column);
        return value == null ? Optional.empty() : Optional.of(value.toLocalDate());
    }

    private static InventoryLot toLot(LotCommon common, Header header) {
        return switch (header.status()) {
            case "manufacturing" -> new ManufacturingLot(common);
            case "manufactured" ->
                new ManufacturedLot(common, header.manufactured().orElseThrow());
            case "shipping_instructed" ->
                new ShippingInstructedLot(
                        common,
                        header.manufactured().orElseThrow(),
                        header.deadline().orElseThrow());
            case "shipped" ->
                new ShippedLot(
                        common,
                        header.manufactured().orElseThrow(),
                        header.deadline().orElseThrow(),
                        header.shipped().orElseThrow());
            case "conversion_instructed" ->
                new ConversionInstructedLot(
                        common,
                        header.manufactured().orElseThrow(),
                        new com.example.salesmanagement.domain.ConversionDestinationInfo(
                                header.destination().orElseThrow()));
            default -> throw new IllegalStateException("Unknown lot status: " + header.status());
        };
    }

    private static Dates dates(InventoryLot lot) {
        return switch (lot) {
            case ManufacturingLot _ -> new Dates(null, null, null, null);
            case ManufacturedLot value -> new Dates(value.manufacturingCompletedDate(), null, null, null);
            case ShippingInstructedLot value ->
                new Dates(value.manufacturingCompletedDate(), value.shippingDeadlineDate(), null, null);
            case ShippedLot value ->
                new Dates(value.manufacturingCompletedDate(), value.shippingDeadlineDate(), value.shippedDate(), null);
            case ConversionInstructedLot value ->
                new Dates(
                        value.manufacturingCompletedDate(),
                        null,
                        null,
                        value.destinationInfo().destinationItem());
        };
    }

    private record Header(
            int divisionCode,
            int departmentCode,
            int sectionCode,
            int processCategory,
            int inspectionCategory,
            int manufacturingCategory,
            String status,
            Optional<LocalDate> manufactured,
            Optional<LocalDate> deadline,
            Optional<LocalDate> shipped,
            Optional<String> destination,
            int version) {}

    private record Dates(LocalDate manufactured, LocalDate deadline, LocalDate shipped, String destination) {}
}
