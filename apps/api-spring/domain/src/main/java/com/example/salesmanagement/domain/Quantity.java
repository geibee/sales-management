package com.example.salesmanagement.domain;

import java.math.BigDecimal;

public final class Quantity {
    private static final BigDecimal MINIMUM = new BigDecimal("0.001");
    private final BigDecimal value;

    private Quantity(BigDecimal value) {
        this.value = value.stripTrailingZeros();
    }

    public BigDecimal value() {
        return value;
    }

    public static Result<Quantity, ValidationError> create(BigDecimal value) {
        if (value.compareTo(MINIMUM) < 0) {
            return Result.failure(new ValidationError("quantity", "Quantity must be >= 0.001"));
        }
        return Result.success(new Quantity(value));
    }

    @Override
    public boolean equals(Object other) {
        return other instanceof Quantity quantity && quantity.value.compareTo(value) == 0;
    }

    @Override
    public int hashCode() {
        return value.hashCode();
    }

    @Override
    public String toString() {
        return value.toPlainString();
    }
}
