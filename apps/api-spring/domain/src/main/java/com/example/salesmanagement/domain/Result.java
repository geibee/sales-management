package com.example.salesmanagement.domain;

import java.util.Optional;

public sealed interface Result<T, E> permits Result.Success, Result.Failure {
    record Success<T, E>(T result) implements Result<T, E> {
        @Override
        public boolean isSuccess() {
            return true;
        }

        @Override
        public Optional<T> value() {
            return Optional.of(result);
        }

        @Override
        public Optional<E> error() {
            return Optional.empty();
        }
    }

    record Failure<T, E>(E reason) implements Result<T, E> {
        @Override
        public boolean isSuccess() {
            return false;
        }

        @Override
        public Optional<T> value() {
            return Optional.empty();
        }

        @Override
        public Optional<E> error() {
            return Optional.of(reason);
        }
    }

    static <T, E> Result<T, E> success(T value) {
        return new Success<>(value);
    }

    static <T, E> Result<T, E> failure(E error) {
        return new Failure<>(error);
    }

    boolean isSuccess();

    default boolean isFailure() {
        return this instanceof Failure<T, E>;
    }

    Optional<T> value();

    Optional<E> error();
}
