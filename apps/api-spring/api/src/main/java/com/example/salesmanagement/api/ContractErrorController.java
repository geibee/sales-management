package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetails;
import jakarta.servlet.RequestDispatcher;
import jakarta.servlet.http.HttpServletRequest;
import org.springframework.boot.webmvc.error.ErrorController;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** MVC より前で拒否された要求も公開契約の ProblemDetails へ正規化する。 */
@RestController
@RequestMapping("${server.error.path:${error.path:/error}}")
public final class ContractErrorController implements ErrorController {
    @RequestMapping
    ResponseEntity<ProblemDetails> error(HttpServletRequest request) {
        HttpStatus status = status(request.getAttribute(RequestDispatcher.ERROR_STATUS_CODE));
        String detail = stringAttribute(request, RequestDispatcher.ERROR_MESSAGE);
        var problem = new ProblemDetails()
                .type(type(status))
                .title(status.getReasonPhrase())
                .status(status.value())
                .detail(detail.isBlank() ? null : detail)
                .errors(null);
        return ResponseEntity.status(status)
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .body(problem);
    }

    private static HttpStatus status(Object value) {
        int code = value instanceof Number number ? number.intValue() : 500;
        HttpStatus result = HttpStatus.resolve(code);
        return result == null ? HttpStatus.INTERNAL_SERVER_ERROR : result;
    }

    private static String stringAttribute(HttpServletRequest request, String name) {
        Object value = request.getAttribute(name);
        return value == null ? "" : value.toString();
    }

    private static String type(HttpStatus status) {
        return switch (status) {
            case BAD_REQUEST -> "bad-request";
            case NOT_FOUND -> "not-found";
            case METHOD_NOT_ALLOWED -> "method-not-allowed";
            case UNSUPPORTED_MEDIA_TYPE -> "unsupported-media-type";
            default -> status.is5xxServerError() ? "internal-error" : "request-error";
        };
    }
}
