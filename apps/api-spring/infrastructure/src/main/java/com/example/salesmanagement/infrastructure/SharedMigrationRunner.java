package com.example.salesmanagement.infrastructure;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.sql.Timestamp;
import java.time.Instant;
import java.util.List;
import javax.sql.DataSource;
import org.springframework.core.io.ClassPathResource;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;
import org.springframework.transaction.support.TransactionTemplate;

public final class SharedMigrationRunner {
    private static final String JOURNAL_DDL =
            """
            CREATE TABLE IF NOT EXISTS schemaversions (
                schemaversionsid SERIAL PRIMARY KEY,
                scriptname VARCHAR(255) NOT NULL,
                applied TIMESTAMP WITHOUT TIME ZONE NOT NULL
            )
            """;

    private final JdbcTemplate jdbc;
    private final TransactionTemplate transaction;

    public SharedMigrationRunner(DataSource dataSource, PlatformTransactionManager transactionManager) {
        this.jdbc = new JdbcTemplate(dataSource);
        this.transaction = new TransactionTemplate(transactionManager);
    }

    public void migrate() {
        jdbc.execute(JOURNAL_DDL);
        var applied = jdbc.query("SELECT scriptname FROM schemaversions", (row, index) -> row.getString(1));
        for (var name : migrationOrder()) {
            if (!applied.contains(name)) {
                apply(name);
            }
        }
    }

    private void apply(String name) {
        transaction.executeWithoutResult(status -> {
            jdbc.execute(read("db/migrations/" + name));
            jdbc.update(
                    "INSERT INTO schemaversions(scriptname, applied) VALUES (?, ?)",
                    name,
                    Timestamp.from(Instant.now()));
        });
    }

    private static List<String> migrationOrder() {
        return read("db/migration-order.txt")
                .lines()
                .filter(line -> !line.isBlank())
                .toList();
    }

    private static String read(String path) {
        try (var stream = new ClassPathResource(path).getInputStream()) {
            return new String(stream.readAllBytes(), StandardCharsets.UTF_8);
        } catch (IOException exception) {
            throw new IllegalStateException("Required classpath resource is missing: " + path, exception);
        }
    }
}
