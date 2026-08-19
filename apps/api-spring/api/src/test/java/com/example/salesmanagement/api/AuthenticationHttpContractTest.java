package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.nimbusds.jose.JWSAlgorithm;
import com.nimbusds.jose.JWSHeader;
import com.nimbusds.jose.crypto.MACSigner;
import com.nimbusds.jwt.JWTClaimsSet;
import com.nimbusds.jwt.SignedJWT;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Instant;
import java.util.Date;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.testcontainers.postgresql.PostgreSQLContainer;

@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "management.server.port=0",
            "sales-management.authentication.enabled=true",
            "sales-management.authentication.signing-key=" + AuthenticationHttpContractTest.SIGNING_KEY,
            "sales-management.authentication.audience=" + AuthenticationHttpContractTest.AUDIENCE,
            "sales-management.authentication.authority=https://idp.example.com/realms/sales",
            "sales-management.outbox.enabled=false"
        })
final class AuthenticationHttpContractTest {
    static final String SIGNING_KEY = "support-fixture-signing-key-please-do-not-use-in-production";
    static final String AUDIENCE = "sales-api";

    private static final PostgreSQLContainer DATABASE = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");

    static {
        DATABASE.start();
    }

    private final HttpClient http = HttpClient.newHttpClient();

    @LocalServerPort
    private int port;

    @DynamicPropertySource
    static void databaseProperties(DynamicPropertyRegistry registry) {
        registry.add("spring.datasource.url", DATABASE::getJdbcUrl);
        registry.add("spring.datasource.username", DATABASE::getUsername);
        registry.add("spring.datasource.password", DATABASE::getPassword);
    }

    @AfterAll
    static void stopDatabase() {
        DATABASE.close();
    }

    @Test
    void enforcesViewerOperatorAndAdminRoleInheritance() throws Exception {
        assertThat(get("/health", null).statusCode()).isEqualTo(200);
        HttpResponse<String> authConfig = get("/auth/config", null);
        assertThat(authConfig.statusCode()).isEqualTo(200);
        assertThat(authConfig.body())
                .contains("\"enabled\":true")
                .contains("\"authority\":\"https://idp.example.com/realms/sales\"")
                .contains("\"audience\":\"sales-api\"")
                .doesNotContainIgnoringCase("signing", "secret", "password");

        HttpResponse<String> unauthenticated = get("/lots", null);
        assertThat(unauthenticated.statusCode()).isEqualTo(401);
        assertThat(unauthenticated.headers().firstValue("WWW-Authenticate")).hasValue("Bearer");
        assertThat(unauthenticated.headers().firstValue("Content-Type").orElse(""))
                .startsWith("application/problem+json");
        assertThat(unauthenticated.body()).contains("\"type\":\"unauthorized\"");

        assertThat(get("/lots", token(List.of(), 3600)).statusCode()).isEqualTo(403);
        assertThat(get("/lots", token(List.of("viewer"), 3600)).statusCode()).isEqualTo(200);
        assertThat(get("/lots", token(List.of("operator"), 3600)).statusCode()).isEqualTo(200);
        assertThat(get("/lots", token(List.of("admin"), 3600)).statusCode()).isEqualTo(200);

        String body =
                """
                {"lotNumber":{"year":2026,"location":"AUTH","seq":1},
                 "divisionCode":1,"departmentCode":1,"sectionCode":1,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """;
        assertThat(post("/lots", body, token(List.of("viewer"), 3600)).statusCode())
                .isEqualTo(403);
        assertThat(post("/lots", body, token(List.of("operator"), 3600)).statusCode())
                .isEqualTo(200);
    }

    @Test
    void rejectsExpiredAndUnacceptableTokens() throws Exception {
        assertThat(get("/lots", token(List.of("viewer"), -60)).statusCode()).isEqualTo(401);
        HttpResponse<String> forbidden = get("/lots", token(List.of("guest"), 3600));
        assertThat(forbidden.statusCode()).isEqualTo(403);
        assertThat(forbidden.body()).contains("\"type\":\"forbidden\"");
    }

    private HttpResponse<String> get(String path, String token) throws Exception {
        return send(HttpRequest.newBuilder(uri(path)).GET(), token);
    }

    private HttpResponse<String> post(String path, String body, String token) throws Exception {
        return send(
                HttpRequest.newBuilder(uri(path))
                        .header("Content-Type", "application/json")
                        .POST(HttpRequest.BodyPublishers.ofString(body)),
                token);
    }

    private HttpResponse<String> send(HttpRequest.Builder builder, String token) throws Exception {
        if (token != null) {
            builder.header("Authorization", "Bearer " + token);
        }
        return http.send(builder.build(), HttpResponse.BodyHandlers.ofString());
    }

    private URI uri(String path) {
        return URI.create("http://localhost:" + port + path);
    }

    private static String token(List<String> roles, long expiresInSeconds) throws Exception {
        Instant now = Instant.now();
        var claims = new JWTClaimsSet.Builder()
                .subject("contract-user")
                .issuer("support-fixture")
                .audience(AUDIENCE)
                .issueTime(Date.from(now))
                .expirationTime(Date.from(now.plusSeconds(expiresInSeconds)))
                .claim("preferred_username", "contract-user")
                .claim("realm_access", Map.of("roles", roles))
                .build();
        var jwt = new SignedJWT(new JWSHeader(JWSAlgorithm.HS256), claims);
        jwt.sign(new MACSigner(SIGNING_KEY.getBytes(java.nio.charset.StandardCharsets.UTF_8)));
        return jwt.serialize();
    }
}
