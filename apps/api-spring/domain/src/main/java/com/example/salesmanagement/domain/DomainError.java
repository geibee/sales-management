package com.example.salesmanagement.domain;

import java.util.List;

public sealed interface DomainError
        permits DomainError.NotFound,
                DomainError.ValidationFailed,
                DomainError.InvalidStateTransition,
                DomainError.OptimisticLockConflict,
                DomainError.UnexpectedFailure {
    record NotFound(String resource, String id) implements DomainError {}

    record ValidationFailed(List<ValidationError> errors) implements DomainError {
        public ValidationFailed {
            errors = List.copyOf(errors);
        }
    }

    record InvalidStateTransition(String detail) implements DomainError {}

    record OptimisticLockConflict(String resource, String id) implements DomainError {}

    record UnexpectedFailure(String detail) implements DomainError {}
}
