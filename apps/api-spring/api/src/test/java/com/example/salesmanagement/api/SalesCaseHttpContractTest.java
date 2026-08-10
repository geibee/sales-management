package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import io.opentelemetry.api.OpenTelemetry;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.extension.ExtendWith;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.system.CapturedOutput;
import org.springframework.boot.test.system.OutputCaptureExtension;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;
import org.testcontainers.postgresql.PostgreSQLContainer;

@SpringBootTest(
        webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT,
        properties = {
            "management.server.port=0",
            "sales-management.authentication.enabled=false",
            "sales-management.outbox.poll-interval-milliseconds=100"
        })
@ExtendWith(OutputCaptureExtension.class)
final class SalesCaseHttpContractTest {
    private static final PostgreSQLContainer DATABASE = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");

    static {
        DATABASE.start();
    }

    private final HttpClient http = HttpClient.newHttpClient();

    @Autowired
    private ObjectMapper json;

    @Autowired
    private JdbcTemplate jdbc;

    @Autowired
    private OpenTelemetry openTelemetry;

    @LocalServerPort
    private int port;

    @DynamicPropertySource
    static void databaseProperties(DynamicPropertyRegistry registry) {
        registry.add("spring.datasource.url", DATABASE::getJdbcUrl);
        registry.add("spring.datasource.username", DATABASE::getUsername);
        registry.add("spring.datasource.password", DATABASE::getPassword);
    }

    @Test
    void persistsAndReturnsEverySalesCaseSubtypeDetail() throws Exception {
        String directLot = createManufacturedLot(91001);
        assertThat(jdbc.queryForObject(
                        """
                        SELECT updated_by
                        FROM lot
                        WHERE lot_number_year = 2026
                          AND lot_number_location = 'HTTP'
                          AND lot_number_seq = 91001
                        """,
                        String.class))
                .isEqualTo("system");
        String direct = createCase("direct", directLot);
        JsonNode initialDirectDetail = get("/sales-cases/" + direct);
        assertExplicitNulls(initialDirectDetail, "appraisal", "contract", "shippingInstruction", "shippingCompletion");
        JsonNode appraised = post(
                "/sales-cases/" + direct + "/appraisals",
                """
                {"type":"normal","appraisalDate":"2026-01-20","deliveryDate":"2026-01-25",
                 "salesMarket":"market","baseUnitPriceDate":"2026-01-01",
                 "periodAdjustmentRateDate":"2026-01-01","counterpartyAdjustmentRateDate":"2026-01-01",
                 "taxExcludedEstimatedTotal":100000,
                 "lotAppraisals":[{"lotNumber":"%s","detailAppraisals":[{"detailIndex":1,
                 "baseUnitPrice":1000,"periodAdjustmentRate":1.0,"counterpartyAdjustmentRate":1.0}]}],
                 "version":1}
                """
                        .formatted(directLot));
        post(
                "/sales-cases/" + direct + "/contracts",
                """
                {"contractDate":"2026-02-01","person":"person","buyer":{"customerNumber":"C001"},
                 "salesType":1,"item":"item","deliveryMethod":"method",
                 "paymentDeferralCondition":"","salesMethod":1,"usage":"",
                 "taxExcludedContractAmount":100000,"consumptionTax":10000,
                 "taxExcludedPaymentAmount":100000,"paymentConsumptionTax":10000,"version":%d}
                """
                        .formatted(appraised.get("version").asInt()));
        assertThat(jdbc.queryForObject(
                        """
                        SELECT payment_deferral_condition IS NULL AND usage_ IS NULL
                        FROM contract
                        WHERE customer_number = 'C001'
                          AND contract_date = DATE '2026-02-01'
                        """,
                        Boolean.class))
                .isTrue();
        JsonNode directDetail = get("/sales-cases/" + direct);
        assertThat(directDetail.at("/appraisal/appraisalDate").asText()).isEqualTo("2026-01-20");
        assertThat(directDetail.at("/contract/customerNumber").asText()).isEqualTo("C001");

        String reservationLot = createManufacturedLot(91002);
        String reservation = createCase("reservation", reservationLot);
        JsonNode initialReservationDetail = get("/sales-cases/" + reservation);
        assertExplicitNulls(initialReservationDetail, "reservationPrice", "determination", "delivery");
        post(
                "/sales-cases/" + reservation + "/reservation/appraisals",
                """
                {"appraisalDate":"2026-01-20","reservedLotInfo":"reserved-info",
                 "reservedAmount":500000,"version":1}
                """);
        post(
                "/sales-cases/" + reservation + "/reservation/determine",
                "{\"determinedDate\":\"2026-01-22\",\"determinedAmount\":480000,\"version\":2}");
        post(
                "/sales-cases/" + reservation + "/reservation/delivery",
                "{\"deliveryDate\":\"2026-01-30\",\"version\":3}");
        JsonNode reservationDetail = get("/sales-cases/" + reservation);
        assertThat(reservationDetail.at("/reservationPrice/reservedLotInfo").asText())
                .isEqualTo("reserved-info");
        assertThat(reservationDetail.at("/determination/determinedAmount").asInt())
                .isEqualTo(480000);
        assertThat(reservationDetail.at("/delivery/deliveredDate").asText()).isEqualTo("2026-01-30");

        String consignmentLot = createManufacturedLot(91003);
        String consignment = createCase("consignment", consignmentLot);
        JsonNode initialConsignmentDetail = get("/sales-cases/" + consignment);
        assertExplicitNulls(initialConsignmentDetail, "consignor", "result");
        post(
                "/sales-cases/" + consignment + "/consignment/designate",
                """
                {"consignorName":"Acme","consignorCode":"C001",
                 "designatedDate":"2026-01-25","version":1}
                """);
        post(
                "/sales-cases/" + consignment + "/consignment/result",
                "{\"resultDate\":\"2026-01-30\",\"resultAmount\":480000,\"version\":2}");
        JsonNode consignmentDetail = get("/sales-cases/" + consignment);
        assertThat(consignmentDetail.at("/consignor/consignorName").asText()).isEqualTo("Acme");
        assertThat(consignmentDetail.at("/result/resultAmount").asInt()).isEqualTo(480000);
    }

    private static void assertExplicitNulls(JsonNode detail, String... fieldNames) {
        for (String fieldName : fieldNames) {
            assertThat(detail.has(fieldName)).as("response field %s", fieldName).isTrue();
            assertThat(detail.get(fieldName).isNull())
                    .as("response field %s", fieldName)
                    .isTrue();
        }
    }

    @Test
    void returnsStableProblemTypesForMalformedMissingAndStaleRequests() throws Exception {
        JsonResponse malformed = request("GET", "/sales-cases/not-an-id", null);
        assertThat(malformed.status()).isEqualTo(400);
        assertThat(malformed.body().get("type").asText()).isEqualTo("bad-request");

        JsonResponse missing = request("GET", "/sales-cases/9999-99-999", null);
        assertThat(missing.status()).isEqualTo(404);
        assertThat(missing.contentType()).startsWith("application/problem+json");
        assertThat(missing.body().get("type").asText()).isEqualTo("not-found");

        String lot = createManufacturedLot(92001);
        post("/lots/" + lot + "/instruct-shipping", "{\"deadline\":\"2026-02-01\",\"version\":2}");
        JsonResponse stale =
                postResponse("/lots/" + lot + "/complete-shipping", "{\"date\":\"2026-02-02\",\"version\":2}");
        assertThat(stale.status()).isEqualTo(409);
        assertThat(stale.body().get("type").asText()).isEqualTo("optimistic-lock-conflict");
    }

    @Test
    void enforcesOpenApiTransportSemantics() throws Exception {
        JsonResponse methodNotAllowed = request("POST", "/health", null);
        assertThat(methodNotAllowed.status()).isEqualTo(405);
        assertThat(methodNotAllowed.header("Allow")).hasValue("GET");
        assertThat(methodNotAllowed.body().get("type").asText()).isEqualTo("method-not-allowed");

        JsonResponse implicitHeadMustNotBypassContract = request("HEAD", "/health", null);
        assertThat(implicitHeadMustNotBypassContract.status()).isEqualTo(405);
        assertThat(implicitHeadMustNotBypassContract.header("Allow")).hasValue("GET");

        JsonResponse extensionMethod = request("QUERY", "/health", null);
        assertThat(extensionMethod.status()).isEqualTo(405);
        assertThat(extensionMethod.header("Allow")).hasValue("GET");
        assertThat(extensionMethod.body().get("type").asText()).isEqualTo("method-not-allowed");

        JsonResponse unsupported = request("POST", "/lots", "{}", "text/plain");
        assertThat(unsupported.status()).isEqualTo(415);
        assertThat(unsupported.body().get("type").asText()).isEqualTo("unsupported-media-type");

        JsonResponse missingContentType = request("POST", "/lots", "{}", null);
        assertThat(missingContentType.status()).isEqualTo(415);
        assertThat(missingContentType.body().get("type").asText()).isEqualTo("unsupported-media-type");

        String hugeBody = "{\"padding\":\"" + "x".repeat(2 * 1024 * 1024) + "\"}";
        JsonResponse payloadTooLarge = request("POST", "/lots", hugeBody, "application/json");
        assertThat(payloadTooLarge.status()).isEqualTo(413);
        assertThat(payloadTooLarge.body().get("type").asText()).isEqualTo("payload-too-large");

        assertThat(request("GET", "/unknown/path", null).status()).isEqualTo(404);
    }

    @Test
    void preservesValidationProblemShapeAndQueryTypeCodes() throws Exception {
        JsonResponse invalidBody = request(
                "POST",
                "/lots",
                """
                {"lotNumber":{"year":-1,"location":"","seq":0},
                 "divisionCode":1,"departmentCode":1,"sectionCode":1,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":-5,"quantity":-1.0}]}
                """);
        assertThat(invalidBody.status()).isEqualTo(400);
        assertThat(invalidBody.body().get("type"))
                .as("ProblemDetails body: %s", invalidBody.body())
                .isNotNull();
        assertThat(invalidBody.body().get("type").asText()).isEqualTo("validation-error");
        assertThat(invalidBody.body().withArray("errors").valueStream().map(error -> error.get("field")
                        .asText()))
                .contains("lotNumber.year", "lotNumber.location", "lotNumber.seq")
                .anyMatch(field -> field.startsWith("details["));

        assertThat(request("GET", "/lots?sort=createdAt", null)
                        .body()
                        .get("type")
                        .asText())
                .isEqualTo("validation-error");
        assertThat(request("GET", "/lots?limit=abc", null).body().get("type").asText())
                .isEqualTo("validation-error");
        assertThat(request("GET", "/sales-cases?limit=1&limit=2", null)
                        .body()
                        .get("type")
                        .asText())
                .isEqualTo("bad-request");

        JsonResponse unknownEmptyName = request("GET", "/lots/export?=false", null);
        assertThat(unknownEmptyName.status()).isEqualTo(400);
        assertThat(unknownEmptyName.contentType()).startsWith("application/problem+json");
        assertThat(unknownEmptyName.body().get("type").asText()).isEqualTo("validation-error");
        assertThat(unknownEmptyName.body().at("/errors/0/field").asText()).isEmpty();
        assertThat(unknownEmptyName.body().at("/errors/0/message").asText()).isEqualTo("unknown query parameter");

        JsonResponse zeroCase = request("GET", "/sales-cases/0-0-0", null);
        assertThat(zeroCase.status()).isEqualTo(404);
        assertThat(zeroCase.body().has("instance")).isFalse();
        assertThat(zeroCase.body().has("errors")).isFalse();

        assertThat(request("GET", "/lots/available?excludeCase=0-0-0", null).status())
                .isEqualTo(200);

        for (String path : List.of(
                "/lots?status=AAA",
                "/lots/export?status=AAA",
                "/lots/export?format=json",
                "/sales-cases?caseType=AAA")) {
            JsonResponse invalidEnum = request("GET", path, null);
            assertThat(invalidEnum.status()).as(path).isEqualTo(400);
            assertThat(invalidEnum.contentType()).as(path).startsWith("application/problem+json");
        }

        JsonResponse nulStatus = request("GET", "/sales-cases?status=%00", null);
        assertThat(nulStatus.status()).isEqualTo(200);
        assertThat(nulStatus.body().withArray("items").size()).isZero();
        assertThat(nulStatus.body().get("total").asInt()).isZero();

        JsonResponse nullLotDetail = request(
                "POST",
                "/lots",
                """
                {"lotNumber":{"year":2026,"location":"NULL","seq":1},
                 "divisionCode":1,"departmentCode":1,"sectionCode":1,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[null]}
                """);
        assertThat(nullLotDetail.status()).isEqualTo(400);
        assertThat(nullLotDetail.contentType()).startsWith("application/problem+json");

        JsonResponse coercedLocation = request(
                "POST",
                "/lots",
                """
                {"lotNumber":{"year":2026,"location":false,"seq":2},
                 "divisionCode":1,"departmentCode":1,"sectionCode":1,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """);
        assertThat(coercedLocation.status()).isEqualTo(400);
        assertThat(coercedLocation.contentType()).startsWith("application/problem+json");

        JsonResponse nullCaseLot = request(
                "POST",
                "/sales-cases",
                "{" + "\"lots\":[null],\"divisionCode\":1,\"salesDate\":\"2026-04-15\",\"caseType\":\"direct\"}");
        assertThat(nullCaseLot.status()).isEqualTo(400);
        assertThat(nullCaseLot.contentType()).startsWith("application/problem+json");

        JsonResponse nullEditedLot = request("PUT", "/sales-cases/0-0-0/lots", "{\"lots\":[null],\"version\":1}");
        assertThat(nullEditedLot.status()).isEqualTo(400);
        assertThat(nullEditedLot.contentType()).startsWith("application/problem+json");

        JsonResponse malformedEncodedPath = request(
                "PUT",
                "/sales-cases/E%C2%BC%C2%96%3B%C2%9D%C2%94%3B%C2%BC%5B_O%C2%94%F2%95%AF%B4j/lots",
                "{\"lots\":[{}],\"version\":1}");
        assertThat(malformedEncodedPath.status()).isEqualTo(400);
        assertThat(malformedEncodedPath.contentType()).startsWith("application/problem+json");
    }

    @Test
    void exposesHealthDocumentationAndSecurityHeaders() throws Exception {
        JsonResponse health = request("GET", "/health", null);
        assertThat(health.status()).isEqualTo(200);
        assertThat(MediaType.parseMediaType(health.contentType()))
                .isEqualTo(MediaType.parseMediaType("application/json;charset=utf-8"));
        assertThat(health.body().at("/status").asText()).isEqualTo("UP");
        assertThat(health.body().at("/checks/postgresql").asText()).isEqualTo("UP");
        assertThat(health.body().at("/checks/self").asText()).isEqualTo("UP");
        assertThat(health.header("X-Content-Type-Options")).hasValue("nosniff");
        assertThat(health.header("Cross-Origin-Resource-Policy")).hasValue("same-origin");
        assertThat(health.header("X-Frame-Options")).isEmpty();
        assertThat(health.header("X-XSS-Protection")).isEmpty();
        assertThat(health.header("Content-Security-Policy")).isEmpty();
        assertThat(health.header("Referrer-Policy")).isEmpty();

        JsonResponse lots = request("GET", "/lots?limit=1", null);
        assertThat(lots.header("X-Frame-Options")).hasValue("DENY");
        assertThat(lots.header("X-XSS-Protection")).hasValue("1; mode=block");
        assertThat(lots.header("Content-Security-Policy")).hasValue("default-src 'none'; frame-ancestors 'none'");
        assertThat(lots.header("Referrer-Policy")).hasValue("no-referrer");

        JsonResponse authConfig = request("GET", "/auth/config", null);
        assertThat(authConfig.status()).isEqualTo(200);
        assertThat(authConfig.body().toString()).isEqualTo("{\"enabled\":false}");

        HttpResponse<String> openapi = rawGet("/openapi.yaml");
        assertThat(openapi.statusCode()).isEqualTo(200);
        assertThat(openapi.headers().firstValue("Content-Type").orElse("")).startsWith("application/yaml");
        assertThat(openapi.body()).contains("openapi:", "Sales Management API", "/lots:");

        HttpResponse<String> swagger = rawGet("/swagger");
        assertThat(swagger.statusCode()).isEqualTo(200);
        assertThat(swagger.body()).contains("swagger-ui", "/openapi.yaml");
    }

    @Test
    void emitsStructuredRequestLogsAndConfiguresOpenTelemetry(CapturedOutput output) throws Exception {
        assertThat(openTelemetry).isNotNull();

        assertThat(rawGet("/health").statusCode()).isEqualTo(200);

        assertThat(output.getOut())
                .contains("\"message\":\"HTTP request completed\"")
                .contains("\"timestamp\":")
                .contains("\"requestId\":")
                .contains("\"traceId\":")
                .contains("\"spanId\":");
    }

    @Test
    void cachesLotReadsAndInvalidatesAfterTransition() throws Exception {
        String lot = "2026-CACHE-93001";
        post(
                "/lots",
                """
                {"lotNumber":{"year":2026,"location":"CACHE","seq":93001},
                 "divisionCode":1,"departmentCode":10,"sectionCode":100,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v1",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """);

        JsonResponse first = request("GET", "/lots/" + lot, null);
        assertThat(first.header("X-Cache")).hasValue("MISS");
        JsonResponse second = request("GET", "/lots/" + lot, null);
        assertThat(second.header("X-Cache")).hasValue("HIT");

        post("/lots/" + lot + "/complete-manufacturing", "{\"date\":\"2026-02-01\",\"version\":1}");
        JsonResponse afterTransition = request("GET", "/lots/" + lot, null);
        assertThat(afterTransition.header("X-Cache")).hasValue("MISS");
        assertThat(afterTransition.body().get("status").asText()).isEqualTo("manufactured");
    }

    @Test
    void persistsAndProcessesOutboxEventAsynchronously() throws Exception {
        String lot = "2026-EVENT-94001";
        post(
                "/lots",
                """
                {"lotNumber":{"year":2026,"location":"EVENT","seq":94001},
                 "divisionCode":1,"departmentCode":10,"sectionCode":100,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v1",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """);
        post("/lots/" + lot + "/complete-manufacturing", "{\"date\":\"2026-02-01\",\"version\":1}");

        long deadline = System.nanoTime() + java.time.Duration.ofSeconds(5).toNanos();
        String status = "pending";
        while (!status.equals("processed") && System.nanoTime() < deadline) {
            List<String> statuses = jdbc.queryForList(
                    "SELECT status FROM outbox_events WHERE payload->>'lotId' = ?", String.class, lot);
            if (!statuses.isEmpty()) {
                status = statuses.getFirst();
            }
            if (!status.equals("processed")) {
                Thread.sleep(25);
            }
        }
        assertThat(status).isEqualTo("processed");
        assertThat(jdbc.queryForObject(
                        "SELECT payload->>'date' FROM outbox_events WHERE payload->>'lotId' = ?", String.class, lot))
                .isEqualTo("2026-02-01");
    }

    private String createManufacturedLot(int sequence) throws Exception {
        String id = "2026-HTTP-" + sequence;
        post(
                "/lots",
                """
                {"lotNumber":{"year":2026,"location":"HTTP","seq":%d},
                 "divisionCode":1,"departmentCode":10,"sectionCode":100,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"v1",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """
                        .formatted(sequence));
        post("/lots/" + id + "/complete-manufacturing", "{\"date\":\"2026-01-10\",\"version\":1}");
        return id;
    }

    private String createCase(String caseType, String lot) throws Exception {
        JsonNode response = post(
                "/sales-cases",
                """
                {"lots":["%s"],"divisionCode":1,"salesDate":"2026-01-15","caseType":"%s"}
                """
                        .formatted(lot, caseType));
        return response.get("salesCaseNumber").asText();
    }

    private JsonNode get(String path) throws Exception {
        JsonResponse response = request("GET", path, null);
        assertThat(response.status()).isEqualTo(200);
        return response.body();
    }

    private JsonNode post(String path, String body) throws Exception {
        JsonResponse response = postResponse(path, body);
        assertThat(response.status()).isEqualTo(200);
        return response.body();
    }

    private JsonResponse postResponse(String path, String body) throws Exception {
        return request("POST", path, body);
    }

    private JsonResponse request(String method, String path, String body) throws IOException, InterruptedException {
        return request(method, path, body, body == null ? null : "application/json");
    }

    private JsonResponse request(String method, String path, String body, String contentType)
            throws IOException, InterruptedException {
        var builder = HttpRequest.newBuilder(URI.create("http://localhost:" + port + path));
        if (body == null) {
            builder.method(method, HttpRequest.BodyPublishers.noBody());
        } else {
            if (contentType != null) {
                builder.header("Content-Type", contentType);
            }
            builder.method(method, HttpRequest.BodyPublishers.ofString(body));
        }
        HttpResponse<String> response = http.send(builder.build(), HttpResponse.BodyHandlers.ofString());
        return new JsonResponse(
                response.statusCode(),
                response.headers().firstValue("Content-Type").orElse(""),
                response.headers(),
                response.body().isEmpty() ? null : json.readTree(response.body()));
    }

    private HttpResponse<String> rawGet(String path) throws IOException, InterruptedException {
        return http.send(
                HttpRequest.newBuilder(URI.create("http://localhost:" + port + path))
                        .GET()
                        .build(),
                HttpResponse.BodyHandlers.ofString());
    }

    private record JsonResponse(int status, String contentType, java.net.http.HttpHeaders headers, JsonNode body) {
        java.util.Optional<String> header(String name) {
            return headers.firstValue(name);
        }
    }
}
