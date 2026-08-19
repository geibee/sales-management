package com.example.salesmanagement.domain;

public final class Count {
    private final int value;

    private Count(int value) {
        this.value = value;
    }

    public int value() {
        return value;
    }

    public static Result<Count, ValidationError> create(int value) {
        if (value < 1) {
            return Result.failure(new ValidationError("count", "Count must be >= 1"));
        }
        return Result.success(new Count(value));
    }

    @Override
    public boolean equals(Object other) {
        return other instanceof Count count && count.value == value;
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
