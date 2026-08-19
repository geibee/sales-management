package com.example.salesmanagement.api;

import static org.assertj.core.api.Assertions.assertThat;

import java.time.Clock;
import java.time.Instant;
import java.time.ZoneOffset;
import org.junit.jupiter.api.Test;
import org.springframework.mock.web.MockFilterChain;
import org.springframework.mock.web.MockHttpServletRequest;
import org.springframework.mock.web.MockHttpServletResponse;

final class FixedWindowRateLimitFilterTest {
    private static final Clock NOW = Clock.fixed(Instant.parse("2026-08-09T00:00:00Z"), ZoneOffset.UTC);

    @Test
    void rejectsAfterPermitLimitAndIncludesRetryAfter() throws Exception {
        var filter = new FixedWindowRateLimitFilter(2, 60, NOW);

        assertThat(invoke(filter, "/lots").getStatus()).isEqualTo(200);
        assertThat(invoke(filter, "/lots").getStatus()).isEqualTo(200);
        MockHttpServletResponse rejected = invoke(filter, "/lots");
        assertThat(rejected.getStatus()).isEqualTo(429);
        assertThat(rejected.getHeader("Retry-After")).isEqualTo("60");
    }

    @Test
    void healthIsNeverRateLimited() throws Exception {
        var filter = new FixedWindowRateLimitFilter(1, 60, NOW);

        for (int index = 0; index < 5; index++) {
            assertThat(invoke(filter, "/health").getStatus()).isEqualTo(200);
        }
    }

    private static MockHttpServletResponse invoke(FixedWindowRateLimitFilter filter, String path) throws Exception {
        var request = new MockHttpServletRequest("GET", path);
        var response = new MockHttpServletResponse();
        filter.doFilter(request, response, new MockFilterChain());
        return response;
    }
}
