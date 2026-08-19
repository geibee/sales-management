package com.example.salesmanagement.testsupport;

import com.example.salesmanagement.contracts.model.CreateLotRequest;
import com.example.salesmanagement.contracts.model.CreateLotRequestDetailsInner;
import com.example.salesmanagement.contracts.model.CreateLotRequestLotNumber;
import java.math.BigDecimal;
import java.util.List;

public final class RequestBuilders {
    private RequestBuilders() {}

    public static CreateLotRequest manufacturingLot(String location, int sequence) {
        var number = new CreateLotRequestLotNumber(2026, location, sequence);
        var detail = new CreateLotRequestDetailsInner(
                CreateLotRequestDetailsInner.ItemCategoryEnum.GENERAL,
                "P01",
                BigDecimal.ONE,
                BigDecimal.ONE,
                BigDecimal.TEN,
                "A",
                1,
                BigDecimal.ONE);
        return new CreateLotRequest(number, 1, 10, 100, 1, 1, 1, List.of(detail));
    }
}
