package com.example.salesmanagement.domain;

import java.time.LocalDate;

public record SalesCaseCommon(
        SalesCaseNumber number, int divisionCode, LocalDate salesDate, NonEmptyList<ManufacturedLot> lots) {}
