package com.example.salesmanagement.domain;

import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.List;
import java.util.Optional;

final class TestLotFactory {
    private TestLotFactory() {}

    static ManufacturedLot manufacturedLot() {
        var detail = new LotDetail(
                ItemCategory.GENERAL,
                Optional.empty(),
                "P01",
                BigDecimal.ONE,
                BigDecimal.ONE,
                BigDecimal.TEN,
                "A",
                Count.create(1).value().orElseThrow(),
                Quantity.create(BigDecimal.ONE).value().orElseThrow(),
                Optional.empty());
        var common = new LotCommon(
                new LotNumber(2026, "F01", 1),
                1,
                10,
                100,
                1,
                1,
                1,
                NonEmptyList.from(List.of(detail)).orElseThrow());
        return new ManufacturedLot(common, LocalDate.parse("2026-08-08"));
    }
}
