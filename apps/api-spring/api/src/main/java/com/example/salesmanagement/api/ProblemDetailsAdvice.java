package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetails;
import com.example.salesmanagement.contracts.model.ProblemDetailsErrorsInner;
import jakarta.validation.ConstraintViolationException;
import java.util.List;
import org.springframework.beans.TypeMismatchException;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpStatusCode;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.validation.method.MethodValidationException;
import org.springframework.validation.method.MethodValidationResult;
import org.springframework.web.HttpMediaTypeNotSupportedException;
import org.springframework.web.HttpRequestMethodNotSupportedException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.context.request.ServletWebRequest;
import org.springframework.web.context.request.WebRequest;
import org.springframework.web.method.annotation.HandlerMethodValidationException;
import org.springframework.web.servlet.mvc.method.annotation.ResponseEntityExceptionHandler;

@RestControllerAdvice
public final class ProblemDetailsAdvice extends ResponseEntityExceptionHandler {
    @ExceptionHandler(ApiProblemException.class)
    ResponseEntity<ProblemDetails> apiProblem(ApiProblemException exception) {
        HttpStatus status = exception.status();
        ProblemDetails body = problem(status, exception.type(), exception.getMessage())
                .errors(exception.errors().orElse(null));
        return ResponseEntity.status(status)
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .body(body);
    }

    @ExceptionHandler(ConstraintViolationException.class)
    ResponseEntity<ProblemDetails> constraintViolation(ConstraintViolationException exception) {
        var errors = exception.getConstraintViolations().stream()
                .map(violation -> new ProblemDetailsErrorsInner()
                        .field(violation.getPropertyPath().toString())
                        .message(violation.getMessage()))
                .toList();
        return typedResponse(HttpStatus.BAD_REQUEST, validationProblem(errors));
    }

    @Override
    protected ResponseEntity<Object> handleMethodArgumentNotValid(
            MethodArgumentNotValidException exception, HttpHeaders headers, HttpStatusCode status, WebRequest request) {
        var errors = exception.getBindingResult().getFieldErrors().stream()
                .map(error ->
                        new ProblemDetailsErrorsInner().field(error.getField()).message(error.getDefaultMessage()))
                .toList();
        return objectResponse(HttpStatus.BAD_REQUEST, validationProblem(errors));
    }

    @Override
    protected ResponseEntity<Object> handleHandlerMethodValidationException(
            HandlerMethodValidationException exception,
            HttpHeaders headers,
            HttpStatusCode status,
            WebRequest request) {
        if (requestPath(request).equals("/lots")) {
            return objectResponse(HttpStatus.BAD_REQUEST, validationProblem(validationErrors(exception)));
        }
        return objectResponse(
                HttpStatus.BAD_REQUEST, problem(HttpStatus.BAD_REQUEST, "bad-request", exception.getMessage()));
    }

    @Override
    protected ResponseEntity<Object> handleMethodValidationException(
            MethodValidationException exception, HttpHeaders headers, HttpStatus status, WebRequest request) {
        return objectResponse(HttpStatus.BAD_REQUEST, validationProblem(validationErrors(exception)));
    }

    @Override
    protected ResponseEntity<Object> handleTypeMismatch(
            TypeMismatchException exception, HttpHeaders headers, HttpStatusCode status, WebRequest request) {
        String type = requestPath(request).equals("/lots") ? "validation-error" : "bad-request";
        return objectResponse(HttpStatus.BAD_REQUEST, problem(HttpStatus.BAD_REQUEST, type, exception.getMessage()));
    }

    @Override
    protected ResponseEntity<Object> handleHttpMessageNotReadable(
            HttpMessageNotReadableException exception, HttpHeaders headers, HttpStatusCode status, WebRequest request) {
        return objectResponse(
                HttpStatus.BAD_REQUEST, problem(HttpStatus.BAD_REQUEST, "bad-request", exception.getMessage()));
    }

    @Override
    protected ResponseEntity<Object> handleHttpMediaTypeNotSupported(
            HttpMediaTypeNotSupportedException exception,
            HttpHeaders headers,
            HttpStatusCode status,
            WebRequest request) {
        return objectResponse(
                HttpStatus.UNSUPPORTED_MEDIA_TYPE,
                problem(HttpStatus.UNSUPPORTED_MEDIA_TYPE, "unsupported-media-type", exception.getMessage()));
    }

    @Override
    protected ResponseEntity<Object> handleHttpRequestMethodNotSupported(
            HttpRequestMethodNotSupportedException exception,
            HttpHeaders headers,
            HttpStatusCode status,
            WebRequest request) {
        var responseHeaders = new HttpHeaders();
        responseHeaders.setContentType(MediaType.APPLICATION_PROBLEM_JSON);
        responseHeaders.setAllow(exception.getSupportedHttpMethods());
        return new ResponseEntity<>(
                problem(HttpStatus.METHOD_NOT_ALLOWED, "method-not-allowed", exception.getMessage()),
                responseHeaders,
                HttpStatus.METHOD_NOT_ALLOWED);
    }

    private static ResponseEntity<ProblemDetails> typedResponse(HttpStatus status, ProblemDetails body) {
        return ResponseEntity.status(status)
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .body(body);
    }

    private static ResponseEntity<Object> objectResponse(HttpStatus status, ProblemDetails body) {
        return ResponseEntity.status(status)
                .contentType(MediaType.APPLICATION_PROBLEM_JSON)
                .body(body);
    }

    private static ProblemDetails validationProblem(List<ProblemDetailsErrorsInner> errors) {
        return new ProblemDetails()
                .type("validation-error")
                .title("Validation failed")
                .status(400)
                .errors(errors);
    }

    private static ProblemDetails problem(HttpStatus status, String type, String detail) {
        return new ProblemDetails()
                .type(type)
                .title(title(type, status))
                .status(status.value())
                .detail(detail)
                .errors(null);
    }

    private static List<ProblemDetailsErrorsInner> validationErrors(MethodValidationResult result) {
        var beanErrors = result.getBeanResults().stream()
                .flatMap(errors -> errors.getFieldErrors().stream())
                .map(error ->
                        new ProblemDetailsErrorsInner().field(error.getField()).message(error.getDefaultMessage()));
        var valueErrors = result.getValueResults().stream()
                .flatMap(parameter -> parameter.getResolvableErrors().stream()
                        .map(error -> new ProblemDetailsErrorsInner()
                                .field(parameter.getMethodParameter().getParameterName())
                                .message(error.getDefaultMessage())));
        return java.util.stream.Stream.concat(beanErrors, valueErrors).toList();
    }

    private static String requestPath(WebRequest request) {
        return request instanceof ServletWebRequest servlet
                ? servlet.getRequest().getRequestURI()
                : "";
    }

    private static String title(String type, HttpStatus status) {
        return switch (type) {
            case "not-found" -> "Resource not found";
            case "invalid-state-transition" -> "Invalid state transition";
            case "optimistic-lock-conflict" -> "Resource was modified by another user";
            case "validation-error" -> "Validation failed";
            case "internal-error" -> "Internal server error";
            default -> status.getReasonPhrase();
        };
    }
}
