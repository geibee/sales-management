package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.InventoryLot;
import com.example.salesmanagement.domain.LotNumber;
import java.util.List;
import java.util.Optional;

public interface LotRepository {
    Optional<VersionedLot> find(LotNumber number);

    SaveResult insert(InventoryLot value, String actor);

    SaveResult update(InventoryLot value, int expectedVersion, String actor, List<DomainEvent> events);
}
