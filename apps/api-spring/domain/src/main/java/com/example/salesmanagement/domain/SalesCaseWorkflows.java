package com.example.salesmanagement.domain;

import java.time.LocalDate;

public final class SalesCaseWorkflows {
    private SalesCaseWorkflows() {}

    public static DirectSalesCase.Appraised createAppraisal(
            String appraisalReference, DirectSalesCase.BeforeAppraisal salesCase) {
        return new DirectSalesCase.Appraised(salesCase.common(), appraisalReference);
    }

    public static DirectSalesCase.Contracted concludeContract(
            String contractReference, DirectSalesCase.Appraised salesCase) {
        return new DirectSalesCase.Contracted(salesCase.common(), salesCase.appraisalReference(), contractReference);
    }

    public static DirectSalesCase.ShippingInstructed instructShipping(
            LocalDate date, DirectSalesCase.Contracted salesCase) {
        return new DirectSalesCase.ShippingInstructed(
                salesCase.common(), salesCase.appraisalReference(), salesCase.contractReference(), date);
    }

    public static DirectSalesCase.Contracted cancelShippingInstruction(DirectSalesCase.ShippingInstructed salesCase) {
        return new DirectSalesCase.Contracted(
                salesCase.common(), salesCase.appraisalReference(), salesCase.contractReference());
    }

    public static DirectSalesCase.ShippingCompleted completeShipping(
            LocalDate date, DirectSalesCase.ShippingInstructed salesCase) {
        return new DirectSalesCase.ShippingCompleted(
                salesCase.common(),
                salesCase.appraisalReference(),
                salesCase.contractReference(),
                salesCase.instructionDate(),
                date);
    }

    public static ReservationSalesCase.Reserved createReservationPrice(
            String reservationPriceReference, ReservationSalesCase.BeforeReservation salesCase) {
        return new ReservationSalesCase.Reserved(salesCase.common(), reservationPriceReference);
    }

    public static ReservationSalesCase.Confirmed confirmReservation(
            LocalDate date, Amount amount, ReservationSalesCase.Reserved salesCase) {
        return new ReservationSalesCase.Confirmed(
                salesCase.common(), salesCase.reservationPriceReference(), date, amount);
    }

    public static ReservationSalesCase.Reserved cancelReservationConfirmation(
            ReservationSalesCase.Confirmed salesCase) {
        return new ReservationSalesCase.Reserved(salesCase.common(), salesCase.reservationPriceReference());
    }

    public static ReservationSalesCase.Delivered deliverReservation(
            LocalDate date, ReservationSalesCase.Confirmed salesCase) {
        return new ReservationSalesCase.Delivered(
                salesCase.common(),
                salesCase.reservationPriceReference(),
                salesCase.determinedDate(),
                salesCase.determinedAmount(),
                date);
    }

    public static ConsignmentSalesCase.Designated designateConsignment(
            ConsignorInfo consignor, ConsignmentSalesCase.BeforeConsignment salesCase) {
        return new ConsignmentSalesCase.Designated(salesCase.common(), consignor);
    }

    public static ConsignmentSalesCase.BeforeConsignment cancelConsignmentDesignation(
            ConsignmentSalesCase.Designated salesCase) {
        return new ConsignmentSalesCase.BeforeConsignment(salesCase.common());
    }

    public static ConsignmentSalesCase.ResultEntered enterConsignmentResult(
            ConsignmentResult result, ConsignmentSalesCase.Designated salesCase) {
        return new ConsignmentSalesCase.ResultEntered(salesCase.common(), salesCase.consignor(), result);
    }
}
