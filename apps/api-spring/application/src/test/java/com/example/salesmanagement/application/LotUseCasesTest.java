package com.example.salesmanagement.application;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.InventoryLot;
import com.example.salesmanagement.domain.LotCommon;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.ManufacturingLot;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import org.junit.jupiter.api.Test;

class LotUseCasesTest {
    @Test
    void completeManufacturingChecksVersionAndSavesWithEvent() {
        var repository = new InMemoryLotRepository(new VersionedLot(TestLots.lot(), 3));
        var useCases = new LotUseCases(repository, () -> "operator-1");

        var result = useCases.completeManufacturing(new LotNumber(2026, "A", 1), LocalDate.parse("2026-02-01"), 3);

        assertTrue(result.isSuccess());
        assertEquals(4, result.value().orElseThrow().version());
        assertEquals(1, repository.events.size());
        assertEquals("operator-1", repository.lastActor);
    }

    private static final class InMemoryLotRepository implements LotRepository {
        private VersionedLot lot;
        private final List<DomainEvent> events = new ArrayList<>();
        private String lastActor = "";

        private InMemoryLotRepository(VersionedLot lot) {
            this.lot = lot;
        }

        @Override
        public Optional<VersionedLot> find(LotNumber number) {
            return Optional.of(lot);
        }

        @Override
        public SaveResult insert(InventoryLot value, String actor) {
            throw new UnsupportedOperationException();
        }

        @Override
        public SaveResult update(InventoryLot value, int expectedVersion, String actor, List<DomainEvent> events) {
            if (lot.version() != expectedVersion) {
                return SaveResult.conflict();
            }
            this.lot = new VersionedLot(value, expectedVersion + 1);
            this.events.addAll(events);
            this.lastActor = actor;
            return SaveResult.saved(this.lot);
        }
    }

    private static final class TestLots {
        private static InventoryLot lot() {
            LotCommon common = TestFixtures.lotCommon();
            return new ManufacturingLot(common);
        }
    }
}
