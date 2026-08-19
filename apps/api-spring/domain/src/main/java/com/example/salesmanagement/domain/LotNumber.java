package com.example.salesmanagement.domain;

public record LotNumber(int year, String location, int sequence) {
    public LotNumber {
        if (location.isEmpty() || location.indexOf('-') >= 0 || location.indexOf('\0') >= 0) {
            throw new IllegalArgumentException("location must be non-empty and contain neither '-' nor NUL");
        }
    }

    public static Result<LotNumber, ValidationError> parse(String value) {
        if (value.indexOf('\0') >= 0) {
            return invalid();
        }
        var parts = value.split("-", -1);
        if (parts.length != 3 || parts[1].isEmpty()) {
            return invalid();
        }
        try {
            return Result.success(new LotNumber(Integer.parseInt(parts[0]), parts[1], Integer.parseInt(parts[2])));
        } catch (IllegalArgumentException exception) {
            return invalid();
        }
    }

    private static Result<LotNumber, ValidationError> invalid() {
        return Result.failure(new ValidationError("lotNumber", "invalid lot number"));
    }

    @Override
    public String toString() {
        return "%d-%s-%03d".formatted(year, location, sequence);
    }
}
