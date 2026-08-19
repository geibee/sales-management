package com.example.salesmanagement.batch;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import com.example.salesmanagement.infrastructure.SharedMigrationRunner;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import java.util.List;
import java.util.concurrent.Executors;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.jdbc.datasource.DriverManagerDataSource;
import org.testcontainers.postgresql.PostgreSQLContainer;

final class BatchExecutionServiceTest {
    private static final PostgreSQLContainer DATABASE = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");

    private static JdbcTemplate jdbc;
    private static BatchExecutionService service;

    @BeforeAll
    static void migrate() {
        DATABASE.start();
        var dataSource =
                new DriverManagerDataSource(DATABASE.getJdbcUrl(), DATABASE.getUsername(), DATABASE.getPassword());
        var transactionManager = new DataSourceTransactionManager(dataSource);
        jdbc = new JdbcTemplate(dataSource);
        new SharedMigrationRunner(dataSource, transactionManager).migrate();
        service = new BatchExecutionService(
                jdbc, transactionManager, Clock.fixed(Instant.parse("2099-04-15T00:00:00Z"), ZoneOffset.UTC), 2);
    }

    @AfterAll
    static void stopDatabase() {
        DATABASE.close();
    }

    @BeforeEach
    void cleanDatabase() {
        jdbc.execute("TRUNCATE lot, batch_job_execution CASCADE");
    }

    @Test
    void monthlyCloseTransitionsManufacturedLotsInChunksAndRecordsCounts() {
        seedManufacturedLots(2099, "MONTHLY", 3, "2099-04-01");

        var result = service.run("monthly-close", "2099-04");

        assertEquals("COMPLETED", result.status());
        assertEquals(new BatchExecutionService.Counts(3, 3, 0), result.counts());
        assertEquals(
                3,
                jdbc.queryForObject(
                        "SELECT COUNT(*) FROM lot WHERE status = 'shipping_instructed'"
                                + " AND shipping_deadline_date = DATE '2099-04-30'",
                        Integer.class));
        assertEquals(0, jdbc.queryForObject("SELECT COUNT(*) FROM batch_chunk_progress", Integer.class));
    }

    @Test
    void rejectsAlreadyRunningAndAlreadyCompletedExecutions() {
        jdbc.update(
                "INSERT INTO batch_job_execution(job_name, job_params, status) VALUES (?, ?, 'RUNNING')",
                "monthly-close",
                "2099-05");

        var running = service.run("monthly-close", "2099-05");
        assertEquals("ALREADY_RUNNING", running.status());
        assertEquals(
                "RUNNING",
                jdbc.queryForObject(
                        "SELECT status FROM batch_job_execution WHERE job_name = ? AND job_params = ?",
                        String.class,
                        "monthly-close",
                        "2099-05"));

        var completed = service.run("monthly-close", "2099-06");
        assertEquals("COMPLETED", completed.status());
        var repeated = service.run("monthly-close", "2099-06");
        assertEquals("ALREADY_COMPLETED", repeated.status());
    }

    @Test
    void concurrentRestartOfFailedExecutionHasExactlyOneWinner() throws Exception {
        jdbc.update("INSERT INTO batch_job_execution(job_name, job_params, status)"
                + " VALUES ('monthly-close', '2099-09', 'FAILED')");

        try (var executor = Executors.newFixedThreadPool(2)) {
            var first = executor.submit(() -> service.tryStart("monthly-close", "2099-09"));
            var second = executor.submit(() -> service.tryStart("monthly-close", "2099-09"));
            var outcomes = List.of(first.get(), second.get());

            assertEquals(
                    1,
                    outcomes.stream()
                            .filter(outcome -> outcome == BatchExecutionService.StartOutcome.STARTED)
                            .count());
            assertEquals(
                    1,
                    outcomes.stream()
                            .filter(outcome -> outcome == BatchExecutionService.StartOutcome.ALREADY_RUNNING)
                            .count());
        }
    }

    @Test
    void failedExecutionRecordsErrorAndCanBeRestarted() {
        assertThrows(IllegalArgumentException.class, () -> service.run("monthly-close", "invalid-month"));
        assertEquals(
                "FAILED",
                jdbc.queryForObject(
                        "SELECT status FROM batch_job_execution WHERE job_name = ? AND job_params = ?",
                        String.class,
                        "monthly-close",
                        "invalid-month"));

        jdbc.update("UPDATE batch_job_execution SET job_params = '2099-07', error_message = 'previous failure'"
                + " WHERE job_name = 'monthly-close'");
        var restarted = service.run("monthly-close", "2099-07");
        assertEquals("COMPLETED", restarted.status());
        assertEquals(
                null,
                jdbc.queryForObject(
                        "SELECT error_message FROM batch_job_execution WHERE job_name = ? AND job_params = ?",
                        String.class,
                        "monthly-close",
                        "2099-07"));
    }

    @Test
    void resumesMonthlyCloseAfterLastCommittedChunk() {
        List<Long> ids = seedManufacturedLots(2099, "RESUME", 4, "2099-08-01");
        jdbc.update("INSERT INTO batch_job_execution(job_name, job_params, status, error_message)"
                + " VALUES ('monthly-close', '2099-08', 'FAILED', 'previous failure')");
        jdbc.update(
                "INSERT INTO batch_chunk_progress(job_name, job_params, last_processed_id, processed_count)"
                        + " VALUES ('monthly-close', '2099-08', ?, 2)",
                ids.get(1));

        var result = service.run("monthly-close", "2099-08");

        assertEquals(new BatchExecutionService.Counts(2, 2, 0), result.counts());
        assertEquals(
                2,
                jdbc.queryForObject(
                        "SELECT COUNT(*) FROM lot WHERE status = 'manufactured' AND id <= ?",
                        Integer.class,
                        ids.get(1)));
        assertEquals(
                2,
                jdbc.queryForObject(
                        "SELECT COUNT(*) FROM lot WHERE status = 'shipping_instructed' AND id > ?",
                        Integer.class,
                        ids.get(1)));
        assertEquals(0, jdbc.queryForObject("SELECT COUNT(*) FROM batch_chunk_progress", Integer.class));
    }

    @Test
    void importsUtf8CsvAndSkipsInvalidRows() throws Exception {
        Path csv = Files.createTempFile("spring-import-lots-", ".csv");
        try {
            Files.writeString(
                    csv,
                    """
                    ロット番号年度,ロット番号保管場所,ロット番号連番,事業部コード,部門コード,担当課コード,工程区分,検査区分,製造区分
                    2099,CSV,1,1,1,1,1,1,1
                    2099,CSV,2,1,1,1,1,1,1
                    2099,CSV,invalid,1,1,1,1,1,1
                    2099,CSV,4,1,1,1,1,1,1
                    """,
                    StandardCharsets.UTF_8);

            var result = service.run("import-lots", csv.toString());

            assertEquals("COMPLETED", result.status());
            assertEquals(new BatchExecutionService.Counts(4, 3, 1), result.counts());
            assertEquals(
                    List.of(1, 2, 4),
                    jdbc.queryForList(
                            "SELECT lot_number_seq FROM lot WHERE lot_number_location = 'CSV'"
                                    + " ORDER BY lot_number_seq",
                            Integer.class));
        } finally {
            Files.deleteIfExists(csv);
        }
    }

    @Test
    void importsWindows31jCsvUsingFsharpEncodingAliases() throws Exception {
        Path csv = Files.createTempFile("spring-import-lots-cp932-", ".csv");
        try {
            Files.writeString(
                    csv,
                    """
                    ロット番号年度,ロット番号保管場所,ロット番号連番,事業部コード,部門コード,担当課コード,工程区分,検査区分,製造区分
                    2099,東京,1,1,1,1,1,1,1
                    """,
                    Charset.forName("windows-31j"));

            var result = service.runImportLots(csv.toString(), "windows-31j");

            assertEquals(new BatchExecutionService.Counts(1, 1, 0), result.counts());
            assertEquals("東京", jdbc.queryForObject("SELECT lot_number_location FROM lot", String.class));
        } finally {
            Files.deleteIfExists(csv);
        }
    }

    private static List<Long> seedManufacturedLots(int year, String location, int count, String completedDate) {
        for (int sequence = 1; sequence <= count; sequence++) {
            jdbc.update(
                    """
                    INSERT INTO lot(
                        lot_number_year, lot_number_location, lot_number_seq,
                        division_code, department_code, section_code,
                        process_category, inspection_category, manufacturing_category,
                        status, manufacturing_completed_date)
                    VALUES (?, ?, ?, 1, 1, 1, 1, 1, 1, 'manufactured', ?::date)
                    """,
                    year,
                    location,
                    sequence,
                    completedDate);
        }
        return jdbc.queryForList(
                "SELECT id FROM lot WHERE lot_number_year = ? AND lot_number_location = ? ORDER BY id",
                Long.class,
                year,
                location);
    }
}
