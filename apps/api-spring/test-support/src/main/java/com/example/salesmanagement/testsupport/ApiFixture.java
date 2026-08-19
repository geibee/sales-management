package com.example.salesmanagement.testsupport;

import java.net.URI;
import org.testcontainers.postgresql.PostgreSQLContainer;

public final class ApiFixture implements AutoCloseable {
    private final PostgreSQLContainer database = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");
    private final URI baseUri;

    public ApiFixture(URI baseUri) {
        this.baseUri = baseUri;
    }

    public void start() {
        database.start();
    }

    public URI baseUri() {
        return baseUri;
    }

    public String jdbcUrl() {
        return database.getJdbcUrl();
    }

    public String username() {
        return database.getUsername();
    }

    public String password() {
        return database.getPassword();
    }

    @Override
    public void close() {
        database.close();
    }
}
