package com.example.salesmanagement.api;

import static org.junit.jupiter.api.Assertions.assertEquals;

import com.example.salesmanagement.contracts.model.PriceCheckResponse;
import com.fasterxml.jackson.databind.ObjectMapper;
import java.math.BigDecimal;
import org.junit.jupiter.api.Test;

final class PriceCheckSerializationTest {
    @Test
    void writesWholeMonetaryAmountWithoutFloatingPointSuffix() throws Exception {
        var response = new PriceCheckResponse(new BigDecimal("10000"), "external-pricing-api")
                .adjustmentRate(new BigDecimal("1.05"));

        assertEquals(
                "{\"basePrice\":10000,\"adjustmentRate\":1.05,\"source\":\"external-pricing-api\"}",
                new ObjectMapper().writeValueAsString(response));
    }
}
