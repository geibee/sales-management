package com.example.salesmanagement.application;

import java.util.List;
import java.util.Optional;

public interface LotQueries {
    LotPage list(Optional<String> status, int limit, int offset);

    List<VersionedLot> listAvailable(Optional<String> excludedSalesCase);

    record LotPage(List<VersionedLot> items, int total, int limit, int offset) {
        public LotPage {
            items = List.copyOf(items);
        }
    }
}
