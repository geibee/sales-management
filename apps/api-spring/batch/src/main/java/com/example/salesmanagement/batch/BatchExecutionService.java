package com.example.salesmanagement.batch;

import java.io.BufferedReader;
import java.io.IOException;
import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Clock;
import java.time.LocalDate;
import java.time.OffsetDateTime;
import java.time.YearMonth;
import java.time.format.DateTimeParseException;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

public final class BatchExecutionService {
    private static final Set<String> JOBS = Set.of("import-lots", "monthly-close", "monitoring", "partition", "outbox");
    private static final int DEFAULT_CHUNK_SIZE = 1000;

    private final JdbcTemplate jdbc;
    private final TransactionTemplate transaction;
    private final Clock clock;
    private final int chunkSize;

    public BatchExecutionService(JdbcTemplate jdbc, PlatformTransactionManager transactionManager, Clock clock) {
        this(jdbc, transactionManager, clock, DEFAULT_CHUNK_SIZE);
    }

    public BatchExecutionService(
            JdbcTemplate jdbc, PlatformTransactionManager transactionManager, Clock clock, int chunkSize) {
        if (chunkSize <= 0) {
            throw new IllegalArgumentException("chunkSize must be positive");
        }
        this.jdbc = jdbc;
        this.transaction = new TransactionTemplate(transactionManager);
        this.clock = clock;
        this.chunkSize = chunkSize;
    }

    public Result run(String jobName, String parameters) {
        return run(jobName, parameters, StandardCharsets.UTF_8);
    }

    public Result runImportLots(String filePath, String encodingName) {
        String normalized =
                switch (encodingName.strip().toLowerCase(Locale.ROOT)) {
                    case "windows-31j", "cp932", "ms932", "ms_kanji" -> "Shift_JIS";
                    default -> encodingName;
                };
        return run("import-lots", filePath, Charset.forName(normalized));
    }

    private Result run(String jobName, String parameters, Charset importCharset) {
        if (!JOBS.contains(jobName)) {
            throw new IllegalArgumentException("Unknown job: " + jobName);
        }
        StartOutcome start = tryStart(jobName, parameters);
        if (start != StartOutcome.STARTED) {
            return new Result(jobName, parameters, start.name(), new Counts(0, 0, 0));
        }
        try {
            Counts counts = execute(jobName, parameters, importCharset);
            complete(jobName, parameters, counts);
            return new Result(jobName, parameters, "COMPLETED", counts);
        } catch (RuntimeException exception) {
            fail(jobName, parameters, exception.getMessage());
            throw exception;
        }
    }

    StartOutcome tryStart(String jobName, String parameters) {
        int restarted = jdbc.update(
                """
                UPDATE batch_job_execution
                   SET status = 'RUNNING', started_at = ?, completed_at = NULL,
                       error_message = NULL, read_count = 0, write_count = 0, skip_count = 0
                 WHERE job_name = ? AND job_params = ? AND status = 'FAILED'
                """,
                OffsetDateTime.now(clock),
                jobName,
                parameters);
        if (restarted > 0) {
            return StartOutcome.STARTED;
        }

        int inserted = jdbc.update(
                """
                INSERT INTO batch_job_execution(job_name, job_params, status, started_at)
                VALUES (?, ?, 'RUNNING', ?)
                ON CONFLICT (job_name, job_params) DO NOTHING
                """,
                jobName,
                parameters,
                OffsetDateTime.now(clock));
        if (inserted > 0) {
            return StartOutcome.STARTED;
        }

        String status = jdbc.queryForObject(
                "SELECT status FROM batch_job_execution WHERE job_name = ? AND job_params = ?",
                String.class,
                jobName,
                parameters);
        return "COMPLETED".equals(status) ? StartOutcome.ALREADY_COMPLETED : StartOutcome.ALREADY_RUNNING;
    }

    private Counts execute(String jobName, String parameters, Charset importCharset) {
        return switch (jobName) {
            case "monitoring" ->
                new Counts(
                        jdbc.queryForObject(
                                "SELECT COUNT(*) FROM outbox_events WHERE processed_at IS NULL", Integer.class),
                        0,
                        0);
            case "monthly-close" -> executeMonthlyClose(parameters);
            case "partition" -> executePartition(parameters);
            case "outbox" ->
                new Counts(
                        jdbc.queryForObject(
                                "SELECT COUNT(*) FROM outbox_events WHERE processed_at IS NULL", Integer.class),
                        0,
                        0);
            case "import-lots" -> executeImport(parameters, importCharset);
            default -> throw new IllegalStateException(jobName);
        };
    }

    private Counts executeMonthlyClose(String parameters) {
        LocalDate deadline;
        try {
            deadline = YearMonth.parse(parameters).atEndOfMonth();
        } catch (DateTimeParseException exception) {
            throw new IllegalArgumentException("targetMonth must be in YYYY-MM format", exception);
        }

        long lastProcessedId = lastProcessedId("monthly-close", parameters);
        int read = 0;
        int written = 0;
        while (true) {
            List<ManufacturedRow> rows = jdbc.query(
                    """
                    SELECT id, manufacturing_completed_date
                      FROM lot
                     WHERE status = 'manufactured' AND id > ?
                     ORDER BY id
                     LIMIT ?
                    """,
                    (result, rowNumber) -> new ManufacturedRow(
                            result.getLong("id"), result.getObject("manufacturing_completed_date", LocalDate.class)),
                    lastProcessedId,
                    chunkSize);
            if (rows.isEmpty()) {
                break;
            }

            long chunkLastId = rows.getLast().id();
            List<Long> eligibleIds = rows.stream()
                    .filter(row -> !row.manufacturingCompletedDate().isAfter(deadline))
                    .map(ManufacturedRow::id)
                    .toList();
            int writtenBefore = written;
            transaction.executeWithoutResult(status -> {
                for (Long id : eligibleIds) {
                    jdbc.update(
                            """
                            UPDATE lot
                               SET status = 'shipping_instructed', shipping_deadline_date = ?
                             WHERE id = ? AND status = 'manufactured'
                            """,
                            deadline,
                            id);
                }
                upsertProgress("monthly-close", parameters, chunkLastId, writtenBefore + eligibleIds.size());
            });
            read += rows.size();
            written += eligibleIds.size();
            lastProcessedId = chunkLastId;
        }
        deleteProgress("monthly-close", parameters);
        return new Counts(read, written, 0);
    }

    private Counts executeImport(String parameters, Charset charset) {
        Path path = Path.of(parameters);
        long lastProcessedLine = lastProcessedId("import-lots", parameters);
        var buffer = new ArrayList<ImportRow>(chunkSize);
        int read = 0;
        int written = 0;
        int skipped = 0;
        long dataLine = 0;
        long lastVisitedLine = lastProcessedLine;
        boolean headerSkipped = false;

        try (BufferedReader reader = Files.newBufferedReader(path, charset)) {
            String raw;
            while ((raw = reader.readLine()) != null) {
                if (raw.isBlank()) {
                    continue;
                }
                if (!headerSkipped) {
                    headerSkipped = true;
                    continue;
                }
                dataLine++;
                if (dataLine <= lastProcessedLine) {
                    continue;
                }
                read++;
                lastVisitedLine = dataLine;
                ImportRow row = parseLine(dataLine, raw);
                if (row == null) {
                    skipped++;
                    continue;
                }
                buffer.add(row);
                if (buffer.size() >= chunkSize) {
                    writeImportChunk(parameters, buffer, lastVisitedLine, written + buffer.size());
                    written += buffer.size();
                    buffer.clear();
                }
            }
        } catch (IOException exception) {
            throw new IllegalArgumentException("failed to read CSV: " + path, exception);
        }

        if (!buffer.isEmpty()) {
            writeImportChunk(parameters, buffer, lastVisitedLine, written + buffer.size());
            written += buffer.size();
        }
        deleteProgress("import-lots", parameters);
        return new Counts(read, written, skipped);
    }

    private static ImportRow parseLine(long lineNumber, String raw) {
        String[] fields = raw.split(",", -1);
        if (fields.length < 9 || fields[1].isBlank()) {
            return null;
        }
        try {
            int year = positiveInteger(fields[0]);
            int sequence = positiveInteger(fields[2]);
            int division = positiveInteger(fields[3]);
            int department = positiveInteger(fields[4]);
            int section = positiveInteger(fields[5]);
            int process = positiveInteger(fields[6]);
            int inspection = positiveInteger(fields[7]);
            int manufacturing = positiveInteger(fields[8]);
            return new ImportRow(
                    lineNumber,
                    year,
                    fields[1].trim(),
                    sequence,
                    division,
                    department,
                    section,
                    process,
                    inspection,
                    manufacturing);
        } catch (NumberFormatException exception) {
            return null;
        }
    }

    private static int positiveInteger(String value) {
        int parsed = Integer.parseInt(value.trim());
        if (parsed <= 0) {
            throw new NumberFormatException("value must be positive");
        }
        return parsed;
    }

    private void writeImportChunk(String parameters, List<ImportRow> rows, long lastLine, int writtenAfter) {
        transaction.executeWithoutResult(status -> {
            for (ImportRow row : rows) {
                jdbc.update(
                        """
                        INSERT INTO lot(
                            lot_number_year, lot_number_location, lot_number_seq,
                            division_code, department_code, section_code,
                            process_category, inspection_category, manufacturing_category, status)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'manufacturing')
                        ON CONFLICT (lot_number_year, lot_number_location, lot_number_seq) DO NOTHING
                        """,
                        row.year(),
                        row.location(),
                        row.sequence(),
                        row.division(),
                        row.department(),
                        row.section(),
                        row.process(),
                        row.inspection(),
                        row.manufacturing());
            }
            upsertProgress("import-lots", parameters, lastLine, writtenAfter);
        });
    }

    private Counts executePartition(String parameters) {
        int read = jdbc.queryForObject("SELECT COUNT(*) FROM lot", Integer.class);
        jdbc.update(
                """
                INSERT INTO batch_chunk_progress(
                    job_name, job_params, partition_id, last_processed_id, processed_count, status)
                VALUES ('partition', ?, 0, COALESCE((SELECT MAX(id) FROM lot), 0), ?, 'COMPLETED')
                ON CONFLICT (job_name, job_params, partition_id) DO UPDATE
                  SET last_processed_id = EXCLUDED.last_processed_id,
                      processed_count = EXCLUDED.processed_count,
                      status = 'COMPLETED', updated_at = NOW()
                """,
                parameters,
                read);
        return new Counts(read, read, 0);
    }

    private long lastProcessedId(String jobName, String parameters) {
        Long value = jdbc.queryForObject(
                """
                SELECT COALESCE(MAX(last_processed_id), 0)
                  FROM batch_chunk_progress
                 WHERE job_name = ? AND job_params = ? AND partition_id = 0
                """,
                Long.class,
                jobName,
                parameters);
        return value == null ? 0 : value;
    }

    private void upsertProgress(String jobName, String parameters, long lastId, int processedCount) {
        jdbc.update(
                """
                INSERT INTO batch_chunk_progress(
                    job_name, job_params, partition_id, last_processed_id, processed_count)
                VALUES (?, ?, 0, ?, ?)
                ON CONFLICT (job_name, job_params, partition_id) DO UPDATE
                  SET last_processed_id = EXCLUDED.last_processed_id,
                      processed_count = EXCLUDED.processed_count,
                      status = 'RUNNING', updated_at = NOW()
                """,
                jobName,
                parameters,
                lastId,
                processedCount);
    }

    private void deleteProgress(String jobName, String parameters) {
        jdbc.update("DELETE FROM batch_chunk_progress WHERE job_name = ? AND job_params = ?", jobName, parameters);
    }

    private void complete(String jobName, String parameters, Counts counts) {
        transaction.executeWithoutResult(status -> jdbc.update(
                """
                UPDATE batch_job_execution
                   SET status = 'COMPLETED', completed_at = ?, read_count = ?, write_count = ?, skip_count = ?,
                       error_message = NULL
                 WHERE job_name = ? AND job_params = ? AND status = 'RUNNING'
                """,
                OffsetDateTime.now(clock),
                counts.read(),
                counts.written(),
                counts.skipped(),
                jobName,
                parameters));
    }

    private void fail(String jobName, String parameters, String message) {
        jdbc.update(
                """
                UPDATE batch_job_execution SET status = 'FAILED', completed_at = ?, error_message = ?
                 WHERE job_name = ? AND job_params = ? AND status = 'RUNNING'
                """,
                OffsetDateTime.now(clock),
                message,
                jobName,
                parameters);
    }

    enum StartOutcome {
        STARTED,
        ALREADY_RUNNING,
        ALREADY_COMPLETED
    }

    private record ManufacturedRow(long id, LocalDate manufacturingCompletedDate) {}

    private record ImportRow(
            long lineNumber,
            int year,
            String location,
            int sequence,
            int division,
            int department,
            int section,
            int process,
            int inspection,
            int manufacturing) {}

    public record Counts(int read, int written, int skipped) {}

    public record Result(String jobName, String parameters, String status, Counts counts) {}
}
