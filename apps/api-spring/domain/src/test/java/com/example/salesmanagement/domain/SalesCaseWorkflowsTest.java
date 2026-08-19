package com.example.salesmanagement.domain;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;

import java.time.LocalDate;
import java.util.List;
import org.junit.jupiter.api.Test;

final class SalesCaseWorkflowsTest {
    @Test
    void directSaleTransitionsOnlyFromAppraisalToContractToShipping() {
        var before = new DirectSalesCase.BeforeAppraisal(common());
        var appraised = SalesCaseWorkflows.createAppraisal("A-1", before);
        var contracted = SalesCaseWorkflows.concludeContract("C-1", appraised);
        var instructed = SalesCaseWorkflows.instructShipping(LocalDate.parse("2026-08-09"), contracted);
        var completed = SalesCaseWorkflows.completeShipping(LocalDate.parse("2026-08-10"), instructed);

        assertEquals("shipping_completed", completed.status());
        assertInstanceOf(DirectSalesCase.Contracted.class, SalesCaseWorkflows.cancelShippingInstruction(instructed));
    }

    @Test
    void cancellingReservationConfirmationPreservesProvisionalPrice() {
        var before = new ReservationSalesCase.BeforeReservation(common());
        var reserved = SalesCaseWorkflows.createReservationPrice("R-1", before);
        var confirmed = SalesCaseWorkflows.confirmReservation(
                LocalDate.parse("2026-08-09"), Amount.create(100).value().orElseThrow(), reserved);

        var cancelled = SalesCaseWorkflows.cancelReservationConfirmation(confirmed);

        assertEquals("reserved", cancelled.status());
        assertEquals("R-1", cancelled.reservationPriceReference());
    }

    @Test
    void cancellingConsignmentDesignationReturnsToInitialState() {
        var before = new ConsignmentSalesCase.BeforeConsignment(common());
        var designated = SalesCaseWorkflows.designateConsignment(
                new ConsignorInfo("委託先", "C01", LocalDate.parse("2026-08-09")), before);

        assertEquals(
                "before_consignment",
                SalesCaseWorkflows.cancelConsignmentDesignation(designated).status());
    }

    private static SalesCaseCommon common() {
        var lot = TestLotFactory.manufacturedLot();
        return new SalesCaseCommon(
                new SalesCaseNumber(2026, 8, 1),
                1,
                LocalDate.parse("2026-08-09"),
                NonEmptyList.from(List.of(lot)).orElseThrow());
    }
}
