package com.example.salesmanagement.domain;

import java.util.Optional;

public record SalesCaseNumber(int year, int month, int sequence) {
    public static Optional<SalesCaseNumber> parse(String value) {
        var parts = value.split("-", -1);
        if (parts.length != 3) {
            return Optional.empty();
        }
        try {
            return Optional.of(new SalesCaseNumber(
                    Integer.parseInt(parts[0]), Integer.parseInt(parts[1]), Integer.parseInt(parts[2])));
        } catch (IllegalArgumentException exception) {
            return Optional.empty();
        }
    }

    @Override
    public String toString() {
        return "%d-%d-%d".formatted(year, month, sequence);
    }
}
