package com.example.salesmanagement.application;

import java.util.Optional;

public sealed interface SaveResult permits SaveResult.Saved, SaveResult.Conflict, SaveResult.Duplicate {
    record Saved(VersionedLot lot) implements SaveResult {}

    record Conflict() implements SaveResult {}

    record Duplicate() implements SaveResult {}

    static SaveResult saved(VersionedLot lot) {
        return new Saved(lot);
    }

    static SaveResult conflict() {
        return new Conflict();
    }

    static SaveResult duplicate() {
        return new Duplicate();
    }

    default Optional<VersionedLot> value() {
        return this instanceof Saved saved ? Optional.of(saved.lot()) : Optional.empty();
    }
}
