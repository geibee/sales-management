package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.example.salesmanagement.contracts.api.DefaultApi;
import com.nimbusds.jose.JWSAlgorithm;
import com.nimbusds.jose.JWSHeader;
import com.nimbusds.jose.crypto.MACSigner;
import com.nimbusds.jwt.JWTClaimsSet;
import com.nimbusds.jwt.SignedJWT;
import java.net.URI;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Instant;
import java.util.Arrays;
import java.util.Date;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.springframework.web.bind.annotation.RequestMapping;
import org.testcontainers.postgresql.PostgreSQLContainer;

@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "management.server.port=0",
            "sales-management.authentication.enabled=true",
            "sales-management.rate-limit.permit-limit=100000",
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

    private final ContractHttpClient http = new ContractHttpClient();

    @LocalServerPort
    private int port;

    static Stream<Arguments> authorizationCases() {
        // OpenAPIから生成された全operationを対象にする。公開APIの例外だけ明示する。
        return Arrays.stream(DefaultApi.class.getDeclaredMethods())
                .map(method -> method.getAnnotation(RequestMapping.class))
                .filter(mapping -> mapping != null)
                .filter(mapping -> !List.of("/health", "/auth/config").contains(mapping.value()[0]))
                .flatMap(mapping -> {
                    String method = mapping.method()[0].name();
                    String path = mapping.value()[0].replace("{id}", "9999-12-999");
                    Stream<Arguments> denied = Stream.of(
                            Arguments.of(method, path, "anonymous", 401, "unauthorized"),
                            Arguments.of(method, path, "no-role", 403, "forbidden"));
                    return method.equals("GET")
                            ? denied
                            : Stream.concat(denied, Stream.of(Arguments.of(method, path, "viewer", 403, "forbidden")));
                });
    }

    @ParameterizedTest(name = "XR-AUTH-001 {0} {1} {2} → {3}")
    @MethodSource("authorizationCases")
    void xrAuth001EveryProtectedOperationRejectsUnauthorizedAccess(
            String method, String path, String role, int status, String problemType) throws Exception {
        var builder = HttpRequest.newBuilder(uri(path));
        if (method.equals("GET")) {
            builder.GET();
        } else {
            builder.header("Content-Type", "application/json")
                    .method(method, HttpRequest.BodyPublishers.ofString("{}"));
        }
        String bearer =
                role.equals("anonymous") ? null : token(role.equals("no-role") ? List.of() : List.of(role), 3600);
        HttpResponse<String> response = send(builder, bearer);
        assertThat(response.statusCode()).isEqualTo(status);
        assertThat(response.headers().firstValue("Content-Type").orElse("")).startsWith("application/problem+json");
        assertThat(response.body()).contains("\"type\":\"" + problemType + "\"");
        if (status == 401) {
            assertThat(response.headers().firstValue("WWW-Authenticate")).hasValue("Bearer");
        }
    }

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
        return http.send(builder.build());
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
