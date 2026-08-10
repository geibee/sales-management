package com.example.salesmanagement.domain;

public record LotCommon(
        LotNumber lotNumber,
        int divisionCode,
        int departmentCode,
        int sectionCode,
        int processCategory,
        int inspectionCategory,
        int manufacturingCategory,
        NonEmptyList<LotDetail> details) {}
