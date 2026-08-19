package com.example.salesmanagement.domain;

import java.util.List;
import java.util.Optional;

public final class NonEmptyList<T> {
    private final List<T> values;

    private NonEmptyList(List<T> values) {
        this.values = List.copyOf(values);
        if (values.isEmpty()) {
            throw new IllegalArgumentException("values must not be empty");
        }
    }

    public static <T> Optional<NonEmptyList<T>> from(List<T> values) {
        return values.isEmpty() ? Optional.empty() : Optional.of(new NonEmptyList<>(values));
    }

    public T head() {
        return values.getFirst();
    }

    public List<T> tail() {
        return values.subList(1, values.size());
    }

    public List<T> values() {
        return values;
    }

    @Override
    public boolean equals(Object other) {
        return other instanceof NonEmptyList<?> list && list.values.equals(values);
    }

    @Override
    public int hashCode() {
        return values.hashCode();
    }

    @Override
    public String toString() {
        return values.toString();
    }
}
