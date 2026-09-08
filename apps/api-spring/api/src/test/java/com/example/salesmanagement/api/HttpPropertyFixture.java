package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.io.IOException;
import java.net.URI;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicInteger;
import org.springframework.boot.builder.SpringApplicationBuilder;
import org.springframework.context.ConfigurableApplicationContext;
import org.springframework.jdbc.core.JdbcTemplate;
import org.testcontainers.postgresql.PostgreSQLContainer;

/** jqwik のコンテナライフサイクルで実 HTTP API と PostgreSQL を共有する。 */
final class HttpPropertyFixture implements AutoCloseable {
    private static final AtomicInteger LOT_SEQUENCE = new AtomicInteger(200000);

    private final ContractHttpClient http = new ContractHttpClient();
    private final PostgreSQLContainer database = new PostgreSQLContainer("postgres:16-alpine")
            .withDatabaseName("sales_management")
            .withUsername("app")
            .withPassword("app");

    private ConfigurableApplicationContext context;
    private JdbcTemplate jdbc;
    private ObjectMapper json;
    private URI baseUri;

    void start() {
        database.start();
        context = new SpringApplicationBuilder(SalesManagementApplication.class)
                .run(
                        "--server.port=0",
                        "--management.server.port=0",
                        "--spring.datasource.url=" + database.getJdbcUrl(),
                        "--spring.datasource.username=" + database.getUsername(),
                        "--spring.datasource.password=" + database.getPassword(),
                        "--logging.level.root=WARN",
                        "--sales-management.authentication.enabled=false",
                        "--sales-management.rate-limit.permit-limit=100000",
                        "--sales-management.outbox.poll-interval-milliseconds=600000");
        jdbc = context.getBean(JdbcTemplate.class);
        json = context.getBean(ObjectMapper.class);
        Integer port = context.getEnvironment().getProperty("local.server.port", Integer.class);
        if (port == null) {
            throw new IllegalStateException("Spring API の local.server.port を取得できません");
        }
        baseUri = URI.create("http://localhost:" + port);
    }

    SeededCase seedCase(String caseType) throws Exception {
        int sequence = LOT_SEQUENCE.incrementAndGet();
        String lot = createLot(sequence);
        expectSuccess(
                request("POST", "/lots/" + lot + "/complete-manufacturing", "{\"date\":\"2026-01-10\",\"version\":1}"));
        JsonNode created = expectSuccess(request(
                "POST",
                "/sales-cases",
                """
                {"lots":["%s"],"divisionCode":1,"salesDate":"2026-01-15","caseType":"%s"}
                """
                        .formatted(lot, caseType)));
        return new SeededCase(created.path("salesCaseNumber").asText(), lot);
    }

    SeededLot seedLotAtPositiveVersion() throws Exception {
        int sequence = LOT_SEQUENCE.incrementAndGet();
        String lot = createLot(sequence);
        expectSuccess(
                request("POST", "/lots/" + lot + "/complete-manufacturing", "{\"date\":\"2026-01-10\",\"version\":1}"));
        expectSuccess(request("POST", "/lots/" + lot + "/cancel-manufacturing-completion", "{\"version\":2}"));
        return new SeededLot(lot, 3);
    }

    JsonNode get(String path) throws Exception {
        JsonResponse response = request("GET", path, null);
        assertThat(response.status()).isEqualTo(200);
        return response.body();
    }

    JsonResponse request(String method, String path, String body) throws IOException, InterruptedException {
        var builder = HttpRequest.newBuilder(baseUri.resolve(path));
        if (body == null) {
            builder.method(method, HttpRequest.BodyPublishers.noBody());
        } else {
            builder.header("Content-Type", "application/json")
                    .method(method, HttpRequest.BodyPublishers.ofString(body));
        }
        HttpResponse<String> response = http.send(builder.build());
        return new JsonResponse(
                response.statusCode(),
                response.headers().firstValue("Content-Type").orElse(""),
                response.body().isEmpty() ? null : json.readTree(response.body()));
    }

    LotSnapshot lotSnapshot(String lot) {
        int sequence = Integer.parseInt(lot.substring(lot.lastIndexOf('-') + 1));
        return new LotSnapshot(
                jdbc.queryForList(
                        """
                        SELECT * FROM lot
                         WHERE lot_number_year=2026 AND lot_number_location='PBT' AND lot_number_seq=?
                        """,
                        sequence),
                jdbc.queryForList(
                        """
                        SELECT * FROM lot_detail
                         WHERE lot_number_year=2026 AND lot_number_location='PBT' AND lot_number_seq=?
                         ORDER BY seq_no
                        """,
                        sequence),
                jdbc.queryForObject("SELECT count(*) FROM outbox_events WHERE payload->>'lotId'=?", Long.class, lot));
    }

    CaseSnapshot caseSnapshot(String caseId) {
        CaseKey key = CaseKey.parse(caseId);
        Object[] parameters = key.parameters();
        List<Map<String, Object>> appraisals = jdbc.queryForList(
                """
                SELECT * FROM appraisal
                 WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                 ORDER BY appraisal_number_year, appraisal_number_month, appraisal_number_seq
                """,
                parameters);
        return new CaseSnapshot(
                jdbc.queryForList(
                        """
                        SELECT * FROM sales_case
                         WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                        """,
                        parameters),
                jdbc.queryForList(
                        """
                        SELECT * FROM sales_case_lot
                         WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                         ORDER BY lot_number_year, lot_number_location, lot_number_seq
                        """,
                        parameters),
                appraisals,
                childRows("lot_appraisal", key),
                childRows("lot_detail_appraisal", key),
                childRows("contract", key),
                jdbc.queryForList(
                        """
                        SELECT * FROM reservation_price
                         WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                         ORDER BY appraisal_number_year, appraisal_number_month, appraisal_number_seq
                        """,
                        parameters),
                jdbc.queryForList(
                        """
                        SELECT * FROM consignment_info
                         WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                        """,
                        parameters),
                jdbc.queryForList(
                        """
                        SELECT * FROM consignment_result
                         WHERE sales_case_number_year=? AND sales_case_number_month=? AND sales_case_number_seq=?
                        """,
                        parameters),
                jdbc.queryForObject("SELECT count(*) FROM outbox_events", Long.class));
    }

    private List<Map<String, Object>> childRows(String table, CaseKey key) {
        String ordering = table.equals("contract")
                ? "c.contract_number_year, c.contract_number_month, c.contract_number_seq"
                : "c.appraisal_number_year, c.appraisal_number_month, c.appraisal_number_seq";
        return jdbc.queryForList(
                "SELECT c.* FROM " + table + " c JOIN appraisal a USING "
                        + "(appraisal_number_year, appraisal_number_month, appraisal_number_seq) "
                        + "WHERE a.sales_case_number_year=? AND a.sales_case_number_month=? "
                        + "AND a.sales_case_number_seq=? ORDER BY " + ordering,
                key.parameters());
    }

    private String createLot(int sequence) throws Exception {
        String lot = "2026-PBT-" + sequence;
        expectSuccess(request("POST", "/lots", lotBody(sequence)));
        return lot;
    }

    private static String lotBody(int sequence) {
        return """
                {"lotNumber":{"year":2026,"location":"PBT","seq":%d},
                 "divisionCode":1,"departmentCode":10,"sectionCode":100,
                 "processCategory":1,"inspectionCategory":1,"manufacturingCategory":1,
                 "details":[{"itemCategory":"general","productCategoryCode":"pbt",
                 "lengthSpecLower":1.0,"thicknessSpecLower":1.0,"thicknessSpecUpper":2.0,
                 "qualityGrade":"A","count":1,"quantity":1.0}]}
                """
                .formatted(sequence);
    }

    private static JsonNode expectSuccess(JsonResponse response) {
        assertThat(response.status()).isBetween(200, 299);
        return response.body();
    }

    @Override
    public void close() {
        if (context != null) {
            context.close();
        }
        database.close();
    }

    record JsonResponse(int status, String contentType, JsonNode body) {}

    record SeededLot(String id, int version) {}

    record SeededCase(String id, String lotId) {}

    record LotSnapshot(List<Map<String, Object>> lot, List<Map<String, Object>> details, Long outboxEvents) {}

    record CaseSnapshot(
            List<Map<String, Object>> salesCase,
            List<Map<String, Object>> lots,
            List<Map<String, Object>> appraisals,
            List<Map<String, Object>> lotAppraisals,
            List<Map<String, Object>> detailAppraisals,
            List<Map<String, Object>> contracts,
            List<Map<String, Object>> reservationPrices,
            List<Map<String, Object>> consignmentInfo,
            List<Map<String, Object>> consignmentResults,
            Long outboxEvents) {}

    private record CaseKey(int year, int month, int sequence) {
        private static CaseKey parse(String value) {
            String[] parts = value.split("-", -1);
            if (parts.length != 3) {
                throw new IllegalArgumentException("Invalid sales case number: " + value);
            }
            return new CaseKey(Integer.parseInt(parts[0]), Integer.parseInt(parts[1]), Integer.parseInt(parts[2]));
        }

        private Object[] parameters() {
            return new Object[] {year, month, sequence};
        }
    }
}
