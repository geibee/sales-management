package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.ConversionDestinationInfo;
import com.example.salesmanagement.domain.ConversionInstructedLot;
import com.example.salesmanagement.domain.DomainError;
import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.LotWorkflows;
import com.example.salesmanagement.domain.ManufacturedLot;
import com.example.salesmanagement.domain.ManufacturingLot;
import com.example.salesmanagement.domain.Result;
import com.example.salesmanagement.domain.ShippingInstructedLot;
import java.time.LocalDate;
import java.util.List;

public final class LotUseCases {
    private final LotRepository repository;
    private final CurrentActor currentActor;

    public LotUseCases(LotRepository repository, CurrentActor currentActor) {
        this.repository = repository;
        this.currentActor = currentActor;
    }

    public Result<VersionedLot, DomainError> completeManufacturing(
            LotNumber number, LocalDate date, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return Result.failure(new DomainError.NotFound("Lot", number.toString()));
        }
        if (!(current.orElseThrow().value() instanceof ManufacturingLot manufacturing)) {
            return Result.failure(new DomainError.InvalidStateTransition("Lot is not in manufacturing state"));
        }
        var updated = LotWorkflows.completeManufacturing(date, manufacturing);
        var event = new DomainEvent.LotManufacturingCompleted(number, date);
        return mapSave(repository.update(updated, expectedVersion, currentActor.userId(), List.of(event)), number);
    }

    public Result<VersionedLot, DomainError> instructShipping(
            LotNumber number, LocalDate deadline, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return notFound(number);
        }
        if (!(current.orElseThrow().value() instanceof ManufacturedLot manufactured)) {
            return invalidState("Lot is not in manufactured state");
        }
        return save(LotWorkflows.instructShipping(deadline, manufactured), expectedVersion, number);
    }

    public Result<VersionedLot, DomainError> completeShipping(LotNumber number, LocalDate date, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return notFound(number);
        }
        if (!(current.orElseThrow().value() instanceof ShippingInstructedLot instructed)) {
            return invalidState("Lot is not in shipping_instructed state");
        }
        return save(LotWorkflows.completeShipping(date, instructed), expectedVersion, number);
    }

    public Result<VersionedLot, DomainError> cancelManufacturingCompletion(LotNumber number, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return notFound(number);
        }
        if (!(current.orElseThrow().value() instanceof ManufacturedLot manufactured)) {
            return invalidState("Lot is not in manufactured state");
        }
        return save(LotWorkflows.cancelManufacturingCompletion(manufactured), expectedVersion, number);
    }

    public Result<VersionedLot, DomainError> instructItemConversion(
            LotNumber number, ConversionDestinationInfo destination, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return notFound(number);
        }
        if (!(current.orElseThrow().value() instanceof ManufacturedLot manufactured)) {
            return invalidState("Lot is not in manufactured state");
        }
        return save(LotWorkflows.instructItemConversion(destination, manufactured), expectedVersion, number);
    }

    public Result<VersionedLot, DomainError> cancelItemConversionInstruction(LotNumber number, int expectedVersion) {
        var current = repository.find(number);
        if (current.isEmpty()) {
            return notFound(number);
        }
        if (!(current.orElseThrow().value() instanceof ConversionInstructedLot instructed)) {
            return invalidState("Lot is not in conversion_instructed state");
        }
        return save(LotWorkflows.cancelItemConversionInstruction(instructed), expectedVersion, number);
    }

    private Result<VersionedLot, DomainError> save(
            com.example.salesmanagement.domain.InventoryLot lot, int expectedVersion, LotNumber number) {
        return mapSave(repository.update(lot, expectedVersion, currentActor.userId(), List.of()), number);
    }

    private static Result<VersionedLot, DomainError> notFound(LotNumber number) {
        return Result.failure(new DomainError.NotFound("Lot", number.toString()));
    }

    private static Result<VersionedLot, DomainError> invalidState(String detail) {
        return Result.failure(new DomainError.InvalidStateTransition(detail));
    }

    private static Result<VersionedLot, DomainError> mapSave(SaveResult result, LotNumber number) {
        return switch (result) {
            case SaveResult.Saved(var lot) -> Result.success(lot);
            case SaveResult.Conflict _ ->
                Result.failure(new DomainError.OptimisticLockConflict("Lot", number.toString()));
            case SaveResult.Duplicate _ ->
                Result.failure(new DomainError.OptimisticLockConflict("Lot", number.toString()));
        };
    }
}
