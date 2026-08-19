package com.example.salesmanagement.domain;

import java.time.LocalDate;

public final class LotWorkflows {
    private LotWorkflows() {}

    public static ManufacturedLot completeManufacturing(LocalDate date, ManufacturingLot lot) {
        return new ManufacturedLot(lot.common(), date);
    }

    public static ShippingInstructedLot instructShipping(LocalDate deadline, ManufacturedLot lot) {
        return new ShippingInstructedLot(lot.common(), lot.manufacturingCompletedDate(), deadline);
    }

    public static ShippedLot completeShipping(LocalDate date, ShippingInstructedLot lot) {
        return new ShippedLot(lot.common(), lot.manufacturingCompletedDate(), lot.shippingDeadlineDate(), date);
    }

    public static ManufacturingLot cancelManufacturingCompletion(ManufacturedLot lot) {
        return new ManufacturingLot(lot.common());
    }

    public static ConversionInstructedLot instructItemConversion(
            ConversionDestinationInfo destination, ManufacturedLot lot) {
        return new ConversionInstructedLot(lot.common(), lot.manufacturingCompletedDate(), destination);
    }

    public static ManufacturedLot cancelItemConversionInstruction(ConversionInstructedLot lot) {
        return new ManufacturedLot(lot.common(), lot.manufacturingCompletedDate());
    }
}
