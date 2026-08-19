package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.model.ProblemDetails;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/** MVC の引数解決より前に query 契約を fail-closed で検査する。 */
@Component
@Order(Ordered.HIGHEST_PRECEDENCE + 10)
public final class QueryGuardFilter extends OncePerRequestFilter {
    private final ObjectMapper objectMapper;
    private final QueryGuardInterceptor guard = new QueryGuardInterceptor();

    public QueryGuardFilter(ObjectMapper objectMapper) {
        this.objectMapper = objectMapper;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain filterChain)
            throws ServletException, IOException {
        try {
            guard.preHandle(request, response, this);
        } catch (ApiProblemException exception) {
            writeProblem(response, exception);
            return;
        }
        filterChain.doFilter(request, response);
    }

    private void writeProblem(HttpServletResponse response, ApiProblemException exception) throws IOException {
        response.setStatus(exception.status().value());
        response.setContentType(MediaType.APPLICATION_PROBLEM_JSON_VALUE);
        var body = new ProblemDetails()
                .type(exception.type())
                .title(
                        exception.type().equals("validation-error")
                                ? "Validation failed"
                                : exception.status().getReasonPhrase())
                .status(exception.status().value())
                .detail(exception.getMessage())
                .errors(exception.errors().orElse(null));
        objectMapper.writeValue(response.getOutputStream(), body);
    }
}
