package com.example.salesmanagement.domain;

public sealed interface InventoryLot
        permits ManufacturingLot, ManufacturedLot, ShippingInstructedLot, ShippedLot, ConversionInstructedLot {
    LotCommon common();

    String status();
}
