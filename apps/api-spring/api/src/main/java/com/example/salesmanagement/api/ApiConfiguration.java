package com.example.salesmanagement.api;

import com.example.salesmanagement.application.CodeMasterQueries;
import com.example.salesmanagement.application.CurrentActor;
import com.example.salesmanagement.application.ExternalPricingGateway;
import com.example.salesmanagement.application.LotQueries;
import com.example.salesmanagement.application.LotUseCases;
import com.example.salesmanagement.application.SalesCaseStore;
import com.example.salesmanagement.contracts.model.ProblemDetails;
import com.example.salesmanagement.infrastructure.HttpExternalPricingGateway;
import com.example.salesmanagement.infrastructure.JdbcCodeMasterQueries;
import com.example.salesmanagement.infrastructure.JdbcLotQueries;
import com.example.salesmanagement.infrastructure.JdbcLotRepository;
import com.example.salesmanagement.infrastructure.JdbcOutboxProcessor;
import com.example.salesmanagement.infrastructure.JdbcSalesCaseStore;
import com.example.salesmanagement.infrastructure.SharedMigrationRunner;
import com.fasterxml.jackson.annotation.JsonInclude;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.datatype.jsr310.JavaTimeModule;
import java.net.URI;
import java.net.http.HttpClient;
import java.time.Clock;
import java.time.Duration;
import java.time.ZoneId;
import java.util.List;
import javax.sql.DataSource;
import org.springframework.boot.ApplicationRunner;
import org.springframework.boot.jackson.autoconfigure.JsonMapperBuilderCustomizer;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;
import org.springframework.core.env.Environment;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.security.authentication.AnonymousAuthenticationToken;
import org.springframework.security.core.context.SecurityContextHolder;
import org.springframework.transaction.PlatformTransactionManager;
import tools.jackson.databind.cfg.CoercionAction;
import tools.jackson.databind.cfg.CoercionInputShape;
import tools.jackson.databind.type.LogicalType;

@Configuration
public class ApiConfiguration {
    @Bean
    ObjectMapper infrastructureObjectMapper() {
        return new ObjectMapper()
                .registerModule(new JavaTimeModule())
                .addMixIn(ProblemDetails.class, NonNullProblemDetails.class);
    }

    @Bean
    JsonMapperBuilderCustomizer problemDetailsJsonCustomizer() {
        return builder -> {
            builder.addMixIn(ProblemDetails.class, NonNullProblemDetails.class);
            builder.withCoercionConfig(LogicalType.Textual, coercion -> {
                coercion.setCoercion(CoercionInputShape.Boolean, CoercionAction.Fail);
                coercion.setCoercion(CoercionInputShape.Integer, CoercionAction.Fail);
                coercion.setCoercion(CoercionInputShape.Float, CoercionAction.Fail);
            });
        };
    }

    @Bean
    SharedMigrationRunner migrationRunner(DataSource dataSource, PlatformTransactionManager transactionManager) {
        return new SharedMigrationRunner(dataSource, transactionManager);
    }

    @Bean
    ApplicationRunner runMigrations(SharedMigrationRunner runner) {
        return arguments -> runner.migrate();
    }

    @Bean
    JdbcLotRepository lotRepository(
            JdbcTemplate jdbc, PlatformTransactionManager transactionManager, ObjectMapper objectMapper) {
        return new JdbcLotRepository(jdbc, transactionManager, objectMapper);
    }

    @Bean
    LotQueries lotQueries(JdbcTemplate jdbc, JdbcLotRepository repository) {
        return new JdbcLotQueries(jdbc, repository);
    }

    @Bean
    CodeMasterQueries codeMasterQueries(JdbcTemplate jdbc) {
        return new JdbcCodeMasterQueries(jdbc);
    }

    @Bean
    SalesCaseStore salesCaseStore(JdbcTemplate jdbc, PlatformTransactionManager transactionManager) {
        return new JdbcSalesCaseStore(jdbc, transactionManager);
    }

    @Bean
    CurrentActor currentActor() {
        return () -> {
            var authentication = SecurityContextHolder.getContext().getAuthentication();
            return authentication == null
                            || !authentication.isAuthenticated()
                            || authentication instanceof AnonymousAuthenticationToken
                    ? "system"
                    : authentication.getName();
        };
    }

    @Bean
    LotUseCases lotUseCases(JdbcLotRepository repository, CurrentActor currentActor) {
        return new LotUseCases(repository, currentActor);
    }

    @Bean
    ExternalPricingGateway externalPricingGateway(ObjectMapper objectMapper, Environment environment) {
        var timeout = Duration.ofMillis(
                environment.getProperty("sales-management.external-pricing.timeout-milliseconds", Long.class, 2000L));
        var client = HttpClient.newBuilder().connectTimeout(timeout).build();
        var baseUri = URI.create(
                environment.getProperty("sales-management.external-pricing.base-url", "http://localhost:8089"));
        return new HttpExternalPricingGateway(client, objectMapper, baseUri, timeout, Clock.systemUTC());
    }

    @Bean
    Clock applicationClock() {
        return Clock.system(ZoneId.of("Asia/Tokyo"));
    }

    @Bean
    JdbcOutboxProcessor outboxProcessor(
            JdbcTemplate jdbc,
            PlatformTransactionManager transactionManager,
            Clock applicationClock,
            Environment environment) {
        var abandonedAfter = Duration.ofMillis(
                environment.getProperty("sales-management.outbox.abandoned-after-milliseconds", Long.class, 300_000L));
        int batchSize = environment.getProperty("sales-management.outbox.batch-size", Integer.class, 100);
        return new JdbcOutboxProcessor(jdbc, transactionManager, applicationClock, abandonedAfter, batchSize);
    }

    @JsonInclude(JsonInclude.Include.NON_NULL)
    @SuppressWarnings("UnusedMethod") // Jackson が mixin の getter を反射で参照する。
    private abstract static class NonNullProblemDetails {
        @JsonInclude(JsonInclude.Include.NON_EMPTY)
        abstract List<?> getErrors();
    }

    @Bean
    ApiInvocationHandler apiInvocationHandler(
            JdbcLotRepository repository,
            LotQueries lotQueries,
            LotUseCases useCases,
            SalesCaseStore salesCaseStore,
            ExternalPricingGateway externalPricingGateway,
            CodeMasterQueries codeMasterQueries,
            Clock applicationClock,
            Environment environment,
            JdbcTemplate jdbc) {
        return new ApiInvocationHandler(
                repository,
                lotQueries,
                useCases,
                salesCaseStore,
                externalPricingGateway,
                codeMasterQueries,
                applicationClock,
                environment,
                jdbc);
    }
}
