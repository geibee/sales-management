package com.example.salesmanagement.domain;

import java.time.LocalDate;

public record ConversionInstructedLot(
        LotCommon common, LocalDate manufacturingCompletedDate, ConversionDestinationInfo destinationInfo)
        implements InventoryLot {
    @Override
    public String status() {
        return "conversion_instructed";
    }
}
