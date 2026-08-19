package com.example.salesmanagement.domain;

import java.time.LocalDate;

public record ShippedLot(
        LotCommon common, LocalDate manufacturingCompletedDate, LocalDate shippingDeadlineDate, LocalDate shippedDate)
        implements InventoryLot {
    @Override
    public String status() {
        return "shipped";
    }
}
