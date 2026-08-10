package com.example.salesmanagement.application;

import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.SalesCaseNumber;
import java.math.BigDecimal;
import java.time.LocalDate;
import java.util.List;
import java.util.Optional;

public interface SalesCaseStore {
    Header create(String caseType, int divisionCode, LocalDate salesDate, List<LotNumber> lots);

    Optional<Header> find(SalesCaseNumber number);

    Page list(Optional<String> status, Optional<String> caseType, int limit, int offset);

    Optional<Header> changeStatus(SalesCaseNumber number, int expectedVersion, String expectedStatus, String newStatus);

    Optional<Header> changeStatusWithDate(
            SalesCaseNumber number,
            int expectedVersion,
            String expectedStatus,
            String newStatus,
            DateField field,
            Optional<LocalDate> date);

    Optional<Header> saveAppraisal(
            SalesCaseNumber number, int expectedVersion, String expectedStatus, DirectAppraisal appraisal);

    Optional<Header> deleteAppraisal(SalesCaseNumber number, int expectedVersion);

    Optional<Header> saveContract(SalesCaseNumber number, int expectedVersion, DirectContract contract);

    Optional<Header> deleteContract(SalesCaseNumber number, int expectedVersion);

    Optional<Header> saveReservationPrice(
            SalesCaseNumber number, int expectedVersion, ReservationPrice reservationPrice);

    Optional<Header> confirmReservation(
            SalesCaseNumber number, int expectedVersion, LocalDate determinedDate, int determinedAmount);

    Optional<Header> cancelReservation(SalesCaseNumber number, int expectedVersion);

    Optional<Header> deliverReservation(SalesCaseNumber number, int expectedVersion, LocalDate deliveredDate);

    Optional<Header> designateConsignment(SalesCaseNumber number, int expectedVersion, Consignor consignor);

    Optional<Header> cancelConsignment(SalesCaseNumber number, int expectedVersion);

    Optional<Header> saveConsignmentResult(SalesCaseNumber number, int expectedVersion, ConsignmentResult result);

    Optional<Header> editLots(SalesCaseNumber number, int expectedVersion, List<LotNumber> lots);

    boolean delete(SalesCaseNumber number, List<String> allowedStatuses);

    enum DateField {
        SHIPPING_INSTRUCTION,
        SHIPPING_COMPLETION
    }

    record Header(
            SalesCaseNumber number,
            String caseType,
            String status,
            int divisionCode,
            LocalDate salesDate,
            int version,
            List<LotNumber> lots,
            Details details) {
        public Header {
            lots = List.copyOf(lots);
        }
    }

    sealed interface Details permits DirectDetails, ReservationDetails, ConsignmentDetails {}

    record DirectDetails(
            Optional<DirectAppraisal> appraisal,
            Optional<DirectContract> contract,
            Optional<LocalDate> shippingInstructionDate,
            Optional<LocalDate> shippingCompletionDate)
            implements Details {}

    record ReservationDetails(Optional<ReservationPrice> reservationPrice) implements Details {}

    record ConsignmentDetails(Optional<Consignor> consignor, Optional<ConsignmentResult> result) implements Details {}

    record DirectAppraisal(
            String type,
            LocalDate appraisalDate,
            LocalDate deliveryDate,
            String salesMarket,
            LocalDate baseUnitPriceDate,
            LocalDate periodAdjustmentRateDate,
            LocalDate counterpartyAdjustmentRateDate,
            int taxExcludedEstimatedTotal,
            Optional<String> customerContractNumber,
            Optional<BigDecimal> contractAdjustmentRate,
            List<LotAppraisal> lotAppraisals) {
        public DirectAppraisal {
            lotAppraisals = List.copyOf(lotAppraisals);
        }
    }

    record LotAppraisal(LotNumber lotNumber, List<DetailAppraisal> detailAppraisals) {
        public LotAppraisal {
            detailAppraisals = List.copyOf(detailAppraisals);
        }
    }

    record DetailAppraisal(
            int detailIndex,
            int baseUnitPrice,
            BigDecimal periodAdjustmentRate,
            BigDecimal counterpartyAdjustmentRate,
            Optional<BigDecimal> exceptionalPeriodAdjustmentRate) {}

    record DirectContract(
            LocalDate contractDate,
            String person,
            String customerNumber,
            Optional<String> agentName,
            int salesType,
            String item,
            String deliveryMethod,
            Optional<String> paymentDeferralCondition,
            int salesMethod,
            Optional<String> usage,
            Optional<Integer> paymentDeferralAmount,
            int taxExcludedContractAmount,
            int consumptionTax,
            int taxExcludedPaymentAmount,
            int paymentConsumptionTax) {}

    record ReservationPrice(
            LocalDate appraisalDate,
            String reservedLotInfo,
            int reservedAmount,
            Optional<LocalDate> determinedDate,
            Optional<Integer> determinedAmount,
            Optional<LocalDate> deliveredDate) {}

    record Consignor(String name, String code, LocalDate designatedDate) {}

    record ConsignmentResult(LocalDate resultDate, int resultAmount) {}

    record Page(List<Header> items, int total, int limit, int offset) {
        public Page {
            items = List.copyOf(items);
        }
    }
}
