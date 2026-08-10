package com.example.salesmanagement.api;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import jakarta.servlet.http.HttpServletResponseWrapper;
import java.io.IOException;
import java.util.Locale;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/** F# 公開契約と同じく JSON の UTF-8 charset を Content-Type に明示する。 */
@Component
@Order(Ordered.HIGHEST_PRECEDENCE + 20)
public final class Utf8JsonContentTypeFilter extends OncePerRequestFilter {
    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain filterChain)
            throws ServletException, IOException {
        filterChain.doFilter(request, new Utf8JsonResponse(response));
    }

    private static String withUtf8Charset(String value) {
        if (value == null || value.toLowerCase(Locale.ROOT).contains("charset=")) {
            return value;
        }
        String mediaType = value.toLowerCase(Locale.ROOT);
        return mediaType.equals("application/json") || mediaType.equals("application/problem+json")
                ? value + "; charset=utf-8"
                : value;
    }

    private static final class Utf8JsonResponse extends HttpServletResponseWrapper {
        Utf8JsonResponse(HttpServletResponse response) {
            super(response);
        }

        @Override
        public void setContentType(String type) {
            super.setContentType(withUtf8Charset(type));
        }

        @Override
        public void setHeader(String name, String value) {
            super.setHeader(name, name.equalsIgnoreCase("Content-Type") ? withUtf8Charset(value) : value);
        }

        @Override
        public void addHeader(String name, String value) {
            super.addHeader(name, name.equalsIgnoreCase("Content-Type") ? withUtf8Charset(value) : value);
        }
    }
}
