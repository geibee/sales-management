package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetailsErrorsInner;
import java.util.List;
import java.util.Optional;
import java.util.stream.IntStream;
import org.springframework.http.HttpStatus;

public final class ApiProblemException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    private final HttpStatus status;
    private final String type;
    private final String[] errorFields;
    private final String[] errorMessages;

    public ApiProblemException(HttpStatus status, String detail) {
        this(status, defaultType(status), detail);
    }

    public ApiProblemException(HttpStatus status, String type, String detail) {
        this(status, type, detail, List.of());
    }

    private ApiProblemException(HttpStatus status, String type, String detail, List<ProblemDetailsErrorsInner> errors) {
        super(detail);
        this.status = status;
        this.type = type;
        this.errorFields =
                errors.stream().map(ProblemDetailsErrorsInner::getField).toArray(String[]::new);
        this.errorMessages =
                errors.stream().map(ProblemDetailsErrorsInner::getMessage).toArray(String[]::new);
    }

    static ApiProblemException validation(List<ProblemDetailsErrorsInner> errors) {
        return new ApiProblemException(HttpStatus.BAD_REQUEST, "validation-error", null, List.copyOf(errors));
    }

    public HttpStatus status() {
        return status;
    }

    public String type() {
        return type;
    }

    public Optional<List<ProblemDetailsErrorsInner>> errors() {
        if (errorFields.length == 0) {
            return Optional.empty();
        }
        return Optional.of(IntStream.range(0, errorFields.length)
                .mapToObj(index -> new ProblemDetailsErrorsInner()
                        .field(errorFields[index])
                        .message(errorMessages[index]))
                .toList());
    }

    private static String defaultType(HttpStatus status) {
        return switch (status) {
            case BAD_REQUEST -> "bad-request";
            case NOT_FOUND -> "not-found";
            case CONFLICT -> "conflict";
            case METHOD_NOT_ALLOWED -> "method-not-allowed";
            case CONTENT_TOO_LARGE -> "payload-too-large";
            case UNSUPPORTED_MEDIA_TYPE -> "unsupported-media-type";
            case INTERNAL_SERVER_ERROR -> "internal-error";
            default -> status.name().toLowerCase(java.util.Locale.ROOT).replace('_', '-');
        };
    }
}
