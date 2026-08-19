package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.LocalDate;
import java.util.List;
import java.util.Optional;
import org.junit.jupiter.api.Test;

class LotWorkflowsTest {
    @Test
    void typedTransitionsPreserveValuesFromManufacturingToShipping() {
        var common = TestLots.common();
        var manufactured =
                LotWorkflows.completeManufacturing(LocalDate.parse("2026-01-10"), new ManufacturingLot(common));
        var instructed = LotWorkflows.instructShipping(LocalDate.parse("2026-01-20"), manufactured);
        var shipped = LotWorkflows.completeShipping(LocalDate.parse("2026-01-18"), instructed);

        assertEquals(common, shipped.common());
        assertEquals(LocalDate.parse("2026-01-10"), shipped.manufacturingCompletedDate());
        assertEquals(LocalDate.parse("2026-01-20"), shipped.shippingDeadlineDate());
        assertEquals(LocalDate.parse("2026-01-18"), shipped.shippedDate());
    }

    @Test
    void itemConversionInstructionCanBeCancelledToManufactured() {
        var common = TestLots.common();
        var manufactured = new ManufacturedLot(common, LocalDate.parse("2026-01-10"));
        var converted = LotWorkflows.instructItemConversion(new ConversionDestinationInfo("ITEM-2"), manufactured);

        assertEquals(manufactured, LotWorkflows.cancelItemConversionInstruction(converted));
    }

    private static final class TestLots {
        private static LotCommon common() {
            var detail = new LotDetail(
                    ItemCategory.GENERAL,
                    Optional.empty(),
                    "P001",
                    new java.math.BigDecimal("1"),
                    new java.math.BigDecimal("1"),
                    new java.math.BigDecimal("2"),
                    "A",
                    Count.create(1).value().orElseThrow(),
                    Quantity.create(new java.math.BigDecimal("1.0")).value().orElseThrow(),
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
}
