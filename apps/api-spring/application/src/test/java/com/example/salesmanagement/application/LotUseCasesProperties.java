package com.example.salesmanagement.application;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;

import com.example.salesmanagement.domain.ConversionDestinationInfo;
import com.example.salesmanagement.domain.DomainError;
import com.example.salesmanagement.domain.DomainEvent;
import com.example.salesmanagement.domain.InventoryLot;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.ManufacturingLot;
import com.example.salesmanagement.domain.Result;
import java.time.LocalDate;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import net.jqwik.api.Arbitraries;
import net.jqwik.api.Arbitrary;
import net.jqwik.api.Combinators;
import net.jqwik.api.Example;
import net.jqwik.api.ForAll;
import net.jqwik.api.Property;
import net.jqwik.api.Provide;

/** 実use caseを操作列で検査する。DB原子性は共通HTTPマトリクスが別途検査する。 */
class LotUseCasesProperties {
    private static final LocalDate DATE = LocalDate.of(2026, 1, 10);

    enum Command {
        COMPLETE_MANUFACTURING("manufacturing", "manufactured"),
        INSTRUCT_SHIPPING("manufactured", "shipping_instructed"),
        COMPLETE_SHIPPING("shipping_instructed", "shipped"),
        CANCEL_MANUFACTURING("manufactured", "manufacturing"),
        INSTRUCT_CONVERSION("manufactured", "conversion_instructed"),
        CANCEL_CONVERSION("conversion_instructed", "manufactured");

        final String source;
        final String target;

        Command(String source, String target) {
            this.source = source;
            this.target = target;
        }

        Result<VersionedLot, DomainError> execute(LotUseCases useCases, LotNumber number, int version) {
            return switch (this) {
                case COMPLETE_MANUFACTURING -> useCases.completeManufacturing(number, DATE, version);
                case INSTRUCT_SHIPPING -> useCases.instructShipping(number, DATE, version);
                case COMPLETE_SHIPPING -> useCases.completeShipping(number, DATE, version);
                case CANCEL_MANUFACTURING -> useCases.cancelManufacturingCompletion(number, version);
                case INSTRUCT_CONVERSION ->
                    useCases.instructItemConversion(number, new ConversionDestinationInfo("別品目"), version);
                case CANCEL_CONVERSION -> useCases.cancelItemConversionInstruction(number, version);
            };
        }
    }

    record Attempt(Command command, boolean stale) {}

    @Property(tries = 100)
    void xrPbt007OperationSequencesPreserveRejectedStateVersionAndEvents(@ForAll("attempts") List<Attempt> attempts) {
        var common = TestFixtures.lotCommon();
        var repository = new RecordingRepository(Optional.of(new VersionedLot(new ManufacturingLot(common), 1)));
        var useCases = new LotUseCases(repository, () -> "review-operator");
        String state = "manufacturing";
        int version = 1;
        int events = 0;

        for (Attempt attempt : attempts) {
            Command command = attempt.command();
            var before = repository.current;
            int writesBefore = repository.writes;
            var result = command.execute(useCases, common.lotNumber(), attempt.stale() ? version - 1 : version);
            if (!command.source.equals(state)) {
                assertInstanceOf(
                        DomainError.InvalidStateTransition.class, result.error().orElseThrow());
                assertEquals(writesBefore, repository.writes, "不正遷移では保存自体を呼び出さない");
                assertEquals(before, repository.current);
            } else if (attempt.stale()) {
                assertInstanceOf(
                        DomainError.OptimisticLockConflict.class, result.error().orElseThrow());
                assertEquals(before, repository.current);
            } else {
                state = command.target;
                version++;
                if (command == Command.COMPLETE_MANUFACTURING) {
                    events++;
                }
                assertEquals(state, result.value().orElseThrow().value().status());
                assertEquals(version, result.value().orElseThrow().version());
                assertEquals(result.value(), repository.current);
                assertEquals("review-operator", repository.lastActor);
            }
            assertEquals(state, repository.current.orElseThrow().value().status());
            assertEquals(version, repository.current.orElseThrow().version());
            assertEquals(common, repository.current.orElseThrow().value().common());
            assertEquals(events, repository.events.size(), "拒否・競合時にイベントを追加しない");
            for (DomainEvent event : repository.events) {
                assertEquals(new DomainEvent.LotManufacturingCompleted(common.lotNumber(), DATE), event);
            }
        }
    }

    @Example
    void xrLot002MissingLotNeverWritesOrPublishesAnEvent() {
        var repository = new RecordingRepository(Optional.empty());
        var useCases = new LotUseCases(repository, () -> "review-operator");
        for (Command command : Command.values()) {
            var result = command.execute(useCases, TestFixtures.lotCommon().lotNumber(), 1);
            assertInstanceOf(DomainError.NotFound.class, result.error().orElseThrow());
            assertEquals(0, repository.writes);
            assertEquals(List.of(), repository.events);
        }
    }

    @Provide
    Arbitrary<List<Attempt>> attempts() {
        return Combinators.combine(Arbitraries.of(Command.values()), Arbitraries.of(false, true))
                .as(Attempt::new)
                .list()
                .ofMinSize(1)
                .ofMaxSize(40);
    }

    private static final class RecordingRepository implements LotRepository {
        private Optional<VersionedLot> current;
        private final List<DomainEvent> events = new ArrayList<>();
        private int writes;
        private String lastActor = "";

        private RecordingRepository(Optional<VersionedLot> current) {
            this.current = current;
        }

        @Override
        public Optional<VersionedLot> find(LotNumber number) {
            return current.filter(lot -> lot.value().common().lotNumber().equals(number));
        }

        @Override
        public SaveResult insert(InventoryLot value, String actor) {
            throw new UnsupportedOperationException("このテストでは新規登録しない");
        }

        @Override
        public SaveResult update(InventoryLot value, int expectedVersion, String actor, List<DomainEvent> newEvents) {
            writes++;
            if (current.orElseThrow().version() != expectedVersion) {
                return SaveResult.conflict();
            }
            var saved = new VersionedLot(value, expectedVersion + 1);
            current = Optional.of(saved);
            events.addAll(newEvents);
            lastActor = actor;
            return SaveResult.saved(saved);
        }
    }
}
