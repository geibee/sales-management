package com.example.salesmanagement.domain;

import java.time.LocalDate;

public record ManufacturedLot(LotCommon common, LocalDate manufacturingCompletedDate) implements InventoryLot {
    @Override
    public String status() {
        return "manufactured";
    }
}
