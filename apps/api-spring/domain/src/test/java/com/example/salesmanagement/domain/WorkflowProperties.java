package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertEquals;

import java.time.LocalDate;
import java.util.List;
import net.jqwik.api.Arbitraries;
import net.jqwik.api.Arbitrary;
import net.jqwik.api.ForAll;
import net.jqwik.api.Property;
import net.jqwik.api.Provide;
import net.jqwik.api.constraints.IntRange;

/** F# の往復性・値保持の検査を、Java の型付き workflow にも適用する。 */
class WorkflowProperties {
    @Property(tries = 100)
    void xrPbt004LotTransitionsPreserveDatesAndCancellationRestoresOriginal(
            @ForAll("dates") LocalDate manufacturedDate,
            @ForAll("dates") LocalDate deadline,
            @ForAll("dates") LocalDate shippedDate,
            @ForAll("references") String destination) {
        var original = new ManufacturingLot(TestLotFactory.manufacturedLot().common());
        var manufactured = LotWorkflows.completeManufacturing(manufacturedDate, original);
        var instructed = LotWorkflows.instructShipping(deadline, manufactured);
        var shipped = LotWorkflows.completeShipping(shippedDate, instructed);

        assertEquals(original.common(), shipped.common());
        assertEquals(manufacturedDate, shipped.manufacturingCompletedDate());
        assertEquals(deadline, shipped.shippingDeadlineDate());
        assertEquals(shippedDate, shipped.shippedDate());
        assertEquals(original, LotWorkflows.cancelManufacturingCompletion(manufactured));

        var converted = LotWorkflows.instructItemConversion(new ConversionDestinationInfo(destination), manufactured);
        assertEquals(destination, converted.destinationInfo().destinationItem());
        assertEquals(manufactured, LotWorkflows.cancelItemConversionInstruction(converted));
    }

    @Property(tries = 100)
    void xrPbt005DirectShippingPreservesAppraisalContractAndDates(
            @ForAll("references") String appraisal,
            @ForAll("references") String contract,
            @ForAll("dates") LocalDate instructionDate,
            @ForAll("dates") LocalDate completedDate) {
        var original = new DirectSalesCase.BeforeAppraisal(common());
        var appraised = SalesCaseWorkflows.createAppraisal(appraisal, original);
        var contracted = SalesCaseWorkflows.concludeContract(contract, appraised);
        var instructed = SalesCaseWorkflows.instructShipping(instructionDate, contracted);
        var completed = SalesCaseWorkflows.completeShipping(completedDate, instructed);

        assertEquals(original.common(), completed.common());
        assertEquals(appraisal, completed.appraisalReference());
        assertEquals(contract, completed.contractReference());
        assertEquals(instructionDate, completed.instructionDate());
        assertEquals(completedDate, completed.completedDate());
        assertEquals(contracted, SalesCaseWorkflows.cancelShippingInstruction(instructed));
    }

    @Property(tries = 100)
    void xrPbt003ReservationDeliveryPreservesPriceAndConfirmation(
            @ForAll("references") String reference,
            @ForAll("dates") LocalDate determinedDate,
            @ForAll("dates") LocalDate deliveryDate,
            @ForAll @IntRange(min = 0, max = 1_000_000) int amountValue) {
        var original = new ReservationSalesCase.BeforeReservation(common());
        var reserved = SalesCaseWorkflows.createReservationPrice(reference, original);
        var amount = Amount.create(amountValue).value().orElseThrow();
        var confirmed = SalesCaseWorkflows.confirmReservation(determinedDate, amount, reserved);
        var delivered = SalesCaseWorkflows.deliverReservation(deliveryDate, confirmed);

        assertEquals(original.common(), delivered.common());
        assertEquals(reference, delivered.reservationPriceReference());
        assertEquals(amount, delivered.determinedAmount());
        assertEquals(determinedDate, delivered.determinedDate());
        assertEquals(deliveryDate, delivered.deliveryDate());
        assertEquals(reserved, SalesCaseWorkflows.cancelReservationConfirmation(confirmed));
    }

    @Property(tries = 100)
    void xrPbt006ConsignmentPreservesConsignorAndResult(
            @ForAll("references") String name,
            @ForAll("references") String code,
            @ForAll("dates") LocalDate designatedDate,
            @ForAll("dates") LocalDate resultDate,
            @ForAll @IntRange(min = 0, max = 1_000_000) int amountValue) {
        var original = new ConsignmentSalesCase.BeforeConsignment(common());
        var consignor = new ConsignorInfo(name, code, designatedDate);
        var designated = SalesCaseWorkflows.designateConsignment(consignor, original);
        var result = new ConsignmentResult(
                resultDate, Amount.create(amountValue).value().orElseThrow());
        var entered = SalesCaseWorkflows.enterConsignmentResult(result, designated);

        assertEquals(original.common(), entered.common());
        assertEquals(consignor, entered.consignor());
        assertEquals(result, entered.result());
        assertEquals(original, SalesCaseWorkflows.cancelConsignmentDesignation(designated));
    }

    @Provide
    Arbitrary<LocalDate> dates() {
        // 月末・閏日と逆順の日付も含む。日付の大小制約は新設しない。
        return Arbitraries.integers().between(0, 4017).map(LocalDate.of(2020, 1, 1)::plusDays);
    }

    @Provide
    Arbitrary<String> references() {
        return Arbitraries.strings().alpha().ofMinLength(1).ofMaxLength(12);
    }

    private static SalesCaseCommon common() {
        return new SalesCaseCommon(
                new SalesCaseNumber(2026, 8, 1),
                1,
                LocalDate.of(2026, 8, 1),
                NonEmptyList.from(List.of(TestLotFactory.manufacturedLot())).orElseThrow());
    }
}
