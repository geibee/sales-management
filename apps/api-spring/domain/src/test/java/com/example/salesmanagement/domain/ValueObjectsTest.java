package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.math.BigDecimal;
import org.junit.jupiter.api.Test;

class ValueObjectsTest {
    @Test
    void amountAcceptsOnlyNonNegativeValues() {
        assertTrue(Amount.create(0).isSuccess());
        assertEquals(
                "Amount must be >= 0", Amount.create(-1).error().orElseThrow().message());
    }

    @Test
    void quantityAcceptsOnlyValuesAtLeastPointZeroZeroOne() {
        assertTrue(Quantity.create(new BigDecimal("0.001")).isSuccess());
        assertTrue(Quantity.create(new BigDecimal("0.0009")).isFailure());
    }

    @Test
    void countAcceptsOnlyPositiveValues() {
        assertTrue(Count.create(1).isSuccess());
        assertTrue(Count.create(0).isFailure());
    }

    @Test
    void lotNumberRoundTripsInFsharpFormat() {
        var number = LotNumber.parse("2026-A-001").value().orElseThrow();
        assertEquals("2026-A-001", number.toString());
        assertTrue(LotNumber.parse("2026-A\u0000-001").isFailure());
    }

    @Test
    void salesCaseNumberAcceptsTheZeroBoundaryAllowedByTheContract() {
        assertEquals(
                new SalesCaseNumber(0, 0, 0), SalesCaseNumber.parse("0-0-0").orElseThrow());
    }
}
