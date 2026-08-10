package com.example.salesmanagement.domain;

public record ManufacturingLot(LotCommon common) implements InventoryLot {
    @Override
    public String status() {
        return "manufacturing";
    }
}
