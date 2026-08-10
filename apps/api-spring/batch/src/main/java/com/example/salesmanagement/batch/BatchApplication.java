package com.example.salesmanagement.batch;

import com.example.salesmanagement.infrastructure.SharedMigrationRunner;
import java.time.Clock;
import javax.sql.DataSource;
import org.springframework.boot.ApplicationRunner;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.context.annotation.Bean;
import org.springframework.core.env.Environment;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.transaction.PlatformTransactionManager;

@SpringBootApplication(scanBasePackages = "com.example.salesmanagement.batch")
public class BatchApplication {
    public static void main(String[] args) {
        SpringApplication.run(BatchApplication.class, args);
    }

    @Bean
    Clock clock() {
        return Clock.systemUTC();
    }

    @Bean
    SharedMigrationRunner migrationRunner(DataSource dataSource, PlatformTransactionManager transactionManager) {
        return new SharedMigrationRunner(dataSource, transactionManager);
    }

    @Bean
    BatchExecutionService executionService(
            JdbcTemplate jdbc, PlatformTransactionManager transactionManager, Clock clock, Environment environment) {
        int chunkSize = environment.getProperty("sales-management.batch.chunk-size", Integer.class, 1000);
        return new BatchExecutionService(jdbc, transactionManager, clock, chunkSize);
    }

    @Bean
    ApplicationRunner launchRequestedJob(SharedMigrationRunner migrations, BatchExecutionService service) {
        return arguments -> {
            migrations.migrate();
            String jobName = requiredOption(arguments, "job");
            if (jobName.equals("import-lots")) {
                service.runImportLots(
                        requiredOption(arguments, "file"), optionalOption(arguments, "encoding", "utf-8"));
            } else {
                String parameters = jobName.equals("monthly-close")
                        ? requiredOption(arguments, "date")
                        : requiredOption(arguments, "parameters");
                service.run(jobName, parameters);
            }
        };
    }

    private static String requiredOption(org.springframework.boot.ApplicationArguments arguments, String name) {
        var values = arguments.getOptionValues(name);
        if (values == null || values.isEmpty() || values.getFirst().isBlank()) {
            throw new IllegalArgumentException("--" + name + " is required");
        }
        return values.getFirst();
    }

    private static String optionalOption(
            org.springframework.boot.ApplicationArguments arguments, String name, String defaultValue) {
        var values = arguments.getOptionValues(name);
        return values == null || values.isEmpty() || values.getFirst().isBlank() ? defaultValue : values.getFirst();
    }
}
