package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.Result;
import java.math.BigDecimal;

public interface ExternalPricingGateway {
    Result<PriceQuote, ExternalPricingError> fetch(String lotId);

    record PriceQuote(BigDecimal basePrice, BigDecimal adjustmentRate, String source) {}

    sealed interface ExternalPricingError {
        record Timeout(int timeoutMilliseconds) implements ExternalPricingError {}

        record CircuitOpen() implements ExternalPricingError {}

        record UpstreamStatus(int status) implements ExternalPricingError {}

        record Malformed(String detail) implements ExternalPricingError {}
    }
}
