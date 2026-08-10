package com.example.salesmanagement.domain;

import java.util.Optional;

public enum ItemCategory {
    GENERAL("general"),
    PREMIUM("premium"),
    CUSTOM("custom");

    private final String wireValue;

    ItemCategory(String wireValue) {
        this.wireValue = wireValue;
    }

    public String wireValue() {
        return wireValue;
    }

    public static Optional<ItemCategory> parse(String value) {
        for (var candidate : values()) {
            if (candidate.wireValue.equals(value)) {
                return Optional.of(candidate);
            }
        }
        return Optional.empty();
    }
}
