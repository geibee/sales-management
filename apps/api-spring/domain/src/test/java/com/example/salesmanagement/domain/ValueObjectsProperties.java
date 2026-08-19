package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertEquals;

import net.jqwik.api.ForAll;
import net.jqwik.api.Property;

class ValueObjectsProperties {
    @Property
    void amountSucceedsExactlyForNonNegativeValues(@ForAll int value) {
        assertEquals(value >= 0, Amount.create(value).isSuccess());
    }

    @Property
    void lotNumberAlwaysRoundTripsValidValues(
            @ForAll("validYears") int year,
            @ForAll("validLocations") String location,
            @ForAll("validSequences") int sequence) {
        var number = new LotNumber(year, location, sequence);
        assertEquals(number, LotNumber.parse(number.toString()).value().orElseThrow());
    }

    @net.jqwik.api.Provide
    net.jqwik.api.Arbitrary<Integer> validYears() {
        return net.jqwik.api.Arbitraries.integers().between(0, 999_999_999);
    }

    @net.jqwik.api.Provide
    net.jqwik.api.Arbitrary<Integer> validSequences() {
        return net.jqwik.api.Arbitraries.integers().between(0, 999_999_999);
    }

    @net.jqwik.api.Provide
    net.jqwik.api.Arbitrary<String> validLocations() {
        return net.jqwik.api.Arbitraries.strings().alpha().ofMinLength(1).ofMaxLength(8);
    }
}
