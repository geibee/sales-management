package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetailsErrorsInner;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.springframework.http.HttpMethod;
import org.springframework.http.HttpStatus;
import org.springframework.web.servlet.HandlerInterceptor;

public final class QueryGuardInterceptor implements HandlerInterceptor {
    private static final Set<String> LOT_STATUSES =
            Set.of("manufacturing", "manufactured", "shipping_instructed", "shipped", "conversion_instructed");
    private static final Set<String> SALES_CASE_TYPES = Set.of("direct", "reservation", "consignment");
    private static final Map<String, Set<String>> ALLOWED = Map.of(
            "/lots", Set.of("status", "limit", "offset"),
            "/lots/export", Set.of("format", "status"),
            "/lots/available", Set.of("excludeCase"),
            "/sales-cases", Set.of("status", "caseType", "limit", "offset"),
            "/api/external/price-check", Set.of("lotId"));

    @Override
    public boolean preHandle(HttpServletRequest request, HttpServletResponse response, Object handler) {
        if (!HttpMethod.GET.matches(request.getMethod())) {
            return true;
        }
        String rawQuery = request.getQueryString();
        if (rawQuery != null
                && java.util.Arrays.stream(rawQuery.split("&", -1))
                        .map(part -> part.split("=", 2)[0])
                        .anyMatch(String::isEmpty)) {
            throw ApiProblemException.validation(
                    List.of(new ProblemDetailsErrorsInner().field("").message("unknown query parameter")));
        }
        String servletPath = request.getServletPath();
        String requestPath = servletPath.isEmpty() ? request.getRequestURI() : servletPath;
        Set<String> allowed = ALLOWED.getOrDefault(requestPath, Set.of());
        var unknown = request.getParameterMap().keySet().stream()
                .filter(key -> !allowed.contains(key))
                .sorted()
                .toList();
        if (!unknown.isEmpty()) {
            throw ApiProblemException.validation(unknown.stream()
                    .map(key -> new ProblemDetailsErrorsInner().field(key).message("unknown query parameter"))
                    .toList());
        }
        var empty = request.getParameterMap().entrySet().stream()
                .filter(entry -> entry.getValue().length == 0
                        || java.util.Arrays.stream(entry.getValue()).anyMatch(String::isEmpty))
                .map(Map.Entry::getKey)
                .sorted()
                .toList();
        if (!empty.isEmpty()) {
            throw ApiProblemException.validation(empty.stream()
                    .map(key -> new ProblemDetailsErrorsInner().field(key).message("query parameter must not be empty"))
                    .toList());
        }
        var duplicates = request.getParameterMap().entrySet().stream()
                .filter(entry -> entry.getValue().length > 1)
                .map(Map.Entry::getKey)
                .sorted()
                .toList();
        if (!duplicates.isEmpty()) {
            String type = requestPath.equals("/lots") ? "validation-error" : "bad-request";
            throw new ApiProblemException(
                    HttpStatus.BAD_REQUEST, type, "query parameter must occur once: " + String.join(", ", duplicates));
        }
        validateEnum(request, requestPath, "status", requestPath.startsWith("/lots") ? LOT_STATUSES : Set.of());
        validateEnum(request, requestPath, "format", requestPath.equals("/lots/export") ? Set.of("csv") : Set.of());
        validateEnum(
                request, requestPath, "caseType", requestPath.equals("/sales-cases") ? SALES_CASE_TYPES : Set.of());
        return true;
    }

    private static void validateEnum(
            HttpServletRequest request, String requestPath, String parameter, Set<String> allowedValues) {
        if (allowedValues.isEmpty() || request.getParameter(parameter) == null) {
            return;
        }
        String value = request.getParameter(parameter);
        if (!allowedValues.contains(value)) {
            throw ApiProblemException.validation(List.of(new ProblemDetailsErrorsInner()
                    .field(parameter)
                    .message("invalid query parameter for " + requestPath)));
        }
    }
}
