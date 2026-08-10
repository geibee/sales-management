package com.example.salesmanagement.domain;

import java.math.BigDecimal;
import java.util.Optional;

public record LotDetail(
        ItemCategory itemCategory,
        Optional<String> premiumCategory,
        String productCategoryCode,
        BigDecimal lengthSpecLower,
        BigDecimal thicknessSpecLower,
        BigDecimal thicknessSpecUpper,
        String qualityGrade,
        Count count,
        Quantity quantity,
        Optional<String> inspectionResultCategory) {}
