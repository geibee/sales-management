package com.example.salesmanagement.domain;

import java.time.LocalDate;

public record ShippingInstructedLot(
        LotCommon common, LocalDate manufacturingCompletedDate, LocalDate shippingDeadlineDate)
        implements InventoryLot {
    @Override
    public String status() {
        return "shipping_instructed";
    }
}
