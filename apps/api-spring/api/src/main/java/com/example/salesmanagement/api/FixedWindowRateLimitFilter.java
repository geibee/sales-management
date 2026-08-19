package com.example.salesmanagement.api;

import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.time.Clock;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.core.Ordered;
import org.springframework.core.annotation.Order;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/** API 全体に適用する固定窓レート制限。ヘルスチェックだけは運用監視のため除外する。 */
@Component
@Order(Ordered.HIGHEST_PRECEDENCE + 20)
public final class FixedWindowRateLimitFilter extends OncePerRequestFilter {
    private final int permitLimit;
    private final int windowSeconds;
    private final Clock clock;
    private long activeWindow = Long.MIN_VALUE;
    private int consumed;

    @Autowired
    public FixedWindowRateLimitFilter(
            @Value("${sales-management.rate-limit.permit-limit:100}") int permitLimit,
            @Value("${sales-management.rate-limit.window-seconds:60}") int windowSeconds) {
        this(permitLimit, windowSeconds, Clock.systemUTC());
    }

    FixedWindowRateLimitFilter(int permitLimit, int windowSeconds, Clock clock) {
        if (permitLimit < 1 || windowSeconds < 1) {
            throw new IllegalArgumentException("rate limit values must be positive");
        }
        this.permitLimit = permitLimit;
        this.windowSeconds = windowSeconds;
        this.clock = clock;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain filterChain)
            throws ServletException, IOException {
        String path = request.getRequestURI();
        if (path.equals("/health") || path.startsWith("/health/") || tryAcquire()) {
            filterChain.doFilter(request, response);
            return;
        }
        response.setStatus(429);
        response.setHeader("Retry-After", Integer.toString(windowSeconds));
    }

    private synchronized boolean tryAcquire() {
        long window = Math.floorDiv(clock.instant().getEpochSecond(), windowSeconds);
        if (window != activeWindow) {
            activeWindow = window;
            consumed = 0;
        }
        if (consumed >= permitLimit) {
            return false;
        }
        consumed++;
        return true;
    }
}
