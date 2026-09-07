package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.atlassian.oai.validator.OpenApiInteractionValidator;
import com.atlassian.oai.validator.model.Request;
import com.atlassian.oai.validator.model.SimpleResponse;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Path;
import java.util.stream.Stream;
import java.util.stream.StreamSupport;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

final class ContractValidationTest {
    static Stream<Arguments> cases() throws Exception {
        var cases = new ObjectMapper()
                .readTree(Path.of(System.getProperty("repository.root"))
                        .resolve("tests/fixtures/response-contracts.json")
                        .toFile());
        return StreamSupport.stream(cases.spliterator(), false)
                .map(item -> Arguments.of(item.path("id").asText(), item));
    }

    @ParameterizedTest(name = "応答契約 {0}")
    @MethodSource("cases")
    void validatesSharedResponseFixtures(String id, JsonNode fixture) {
        String spec =
                """
                {"openapi":"3.0.3","info":{"title":"fixture","version":"1"},
                 "paths":{"/fixture":{"get":{"operationId":"fixture","responses":{
                 "%s":{"description":"fixture","content":{"%s":{"schema":%s}}}}}}}}
                """
                        .formatted(
                                fixture.path("responseStatus").asText("200"),
                                fixture.path("mediaType").asText("application/json"),
                                fixture.path("schema"));
        var validator = OpenApiInteractionValidator.createForInlineApiSpecification(spec)
                .build();
        var response = SimpleResponse.Builder.status(fixture.path("status").asInt(200))
                .withBody(fixture.path("body").asText());
        String contentType = fixture.path("contentType").asText("application/json");
        if (!contentType.isEmpty()) {
            response.withHeader("Content-Type", contentType);
        }
        var report = ResponseContract.validate(validator, "/fixture", Request.Method.GET, response.build());
        assertThat(!report.hasErrors())
                .as("%s: %s", id, report)
                .isEqualTo(fixture.path("valid").asBoolean());
    }
}
