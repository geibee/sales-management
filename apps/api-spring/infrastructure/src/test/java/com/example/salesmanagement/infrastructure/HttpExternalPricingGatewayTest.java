package com.example.salesmanagement.infrastructure;

import static org.junit.jupiter.api.Assertions.assertEquals;

import org.junit.jupiter.api.Test;

final class HttpExternalPricingGatewayTest {
    @Test
    void percentEncodesEveryUnsafePathSegmentByte() {
        assertEquals("4-g0%C2%9FS-885504910", HttpExternalPricingGateway.encodePathSegment("4-g0\u009fS-885504910"));
        assertEquals("2026-A%20B%2B%25-001", HttpExternalPricingGateway.encodePathSegment("2026-A B+%-001"));
    }
}
