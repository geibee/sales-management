package com.example.salesmanagement.tools;

import com.example.salesmanagement.infrastructure.SharedMigrationRunner;
import javax.sql.DataSource;
import org.springframework.boot.WebApplicationType;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.context.annotation.Bean;
import org.springframework.jdbc.datasource.DataSourceTransactionManager;
import org.springframework.transaction.PlatformTransactionManager;

@SpringBootApplication
public class MigratorMain {
    public static void main(String[] args) {
        try (var context = new SpringApplicationBuilder(MigratorMain.class)
                .web(WebApplicationType.NONE)
                .run(args)) {
            context.getBean(SharedMigrationRunner.class).migrate();
        }
    }

    @Bean
    PlatformTransactionManager transactionManager(DataSource dataSource) {
        return new DataSourceTransactionManager(dataSource);
    }

    @Bean
    SharedMigrationRunner migrationRunner(DataSource dataSource, PlatformTransactionManager transactionManager) {
        return new SharedMigrationRunner(dataSource, transactionManager);
    }
}
