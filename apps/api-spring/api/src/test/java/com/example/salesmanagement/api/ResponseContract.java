package com.example.salesmanagement.api;

import com.atlassian.oai.validator.OpenApiInteractionValidator;
import com.atlassian.oai.validator.model.Request;
import com.atlassian.oai.validator.model.Response;
import com.atlassian.oai.validator.report.ValidationReport;

/** ライブラリが黙認する Content-Type 欠落を共通契約に従って拒否する。 */
final class ResponseContract {
    private ResponseContract() {}

    static ValidationReport validate(
            OpenApiInteractionValidator validator, String path, Request.Method method, Response response) {
        var report = validator.validateResponse(path, method, response);
        if (response.getResponseBody()
                        .filter(com.atlassian.oai.validator.model.Body::hasBody)
                        .isPresent()
                && response.getHeaderValues("Content-Type").isEmpty()) {
            return report.merge(ValidationReport.singleton(
                    ValidationReport.Message.create("response.contentType.missing", "body がある応答には Content-Type が必須です")
                            .build()
                            .withLevel(ValidationReport.Level.ERROR)));
        }
        return report;
    }
}
