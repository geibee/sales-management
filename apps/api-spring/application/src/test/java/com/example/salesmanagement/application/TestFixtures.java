package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.Count;
import com.example.salesmanagement.domain.ItemCategory;
import com.example.salesmanagement.domain.LotCommon;
import com.example.salesmanagement.domain.LotDetail;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.NonEmptyList;
import com.example.salesmanagement.domain.Quantity;
import java.math.BigDecimal;
import java.util.List;
import java.util.Optional;

final class TestFixtures {
    private TestFixtures() {}

    static LotCommon lotCommon() {
        var detail = new LotDetail(
                ItemCategory.GENERAL,
                Optional.empty(),
                "P001",
                BigDecimal.ONE,
                BigDecimal.ONE,
                BigDecimal.TWO,
                "A",
                Count.create(1).value().orElseThrow(),
                Quantity.create(BigDecimal.ONE).value().orElseThrow(),
                Optional.empty());
        return new LotCommon(
                new LotNumber(2026, "A", 1),
                1,
                10,
                100,
                1,
                1,
                1,
                NonEmptyList.from(List.of(detail)).orElseThrow());
    }
}
