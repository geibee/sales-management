package com.example.salesmanagement.domain;

public final class Amount {
    private final int value;

    private Amount(int value) {
        this.value = value;
    }

    public int value() {
        return value;
    }

    public static Result<Amount, ValidationError> create(int value) {
        if (value < 0) {
            return Result.failure(new ValidationError("amount", "Amount must be >= 0"));
        }
        return Result.success(new Amount(value));
    }

    @Override
    public boolean equals(Object other) {
        return other instanceof Amount amount && amount.value == value;
    }

    @Override
    public int hashCode() {
        return Integer.hashCode(value);
    }

    @Override
    public String toString() {
        return Integer.toString(value);
    }
}
