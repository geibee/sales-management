package com.example.salesmanagement.domain;

import java.time.LocalDate;

public sealed interface DomainEvent permits DomainEvent.LotManufacturingCompleted {
    String eventType();

    record LotManufacturingCompleted(LotNumber lotNumber, LocalDate date) implements DomainEvent {
        @Override
        public String eventType() {
            return "LotManufacturingCompleted";
        }
    }
}
