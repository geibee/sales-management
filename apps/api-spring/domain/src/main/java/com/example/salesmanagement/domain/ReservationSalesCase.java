package com.example.salesmanagement.domain;

import java.time.LocalDate;

public sealed interface ReservationSalesCase {
    SalesCaseCommon common();

    String status();

    record BeforeReservation(SalesCaseCommon common) implements ReservationSalesCase {
        @Override
        public String status() {
            return "before_reservation";
        }
    }

    record Reserved(SalesCaseCommon common, String reservationPriceReference) implements ReservationSalesCase {
        @Override
        public String status() {
            return "reserved";
        }
    }

    record Confirmed(
            SalesCaseCommon common, String reservationPriceReference, LocalDate determinedDate, Amount determinedAmount)
            implements ReservationSalesCase {
        @Override
        public String status() {
            return "reservation_confirmed";
        }
    }

    record Delivered(
            SalesCaseCommon common,
            String reservationPriceReference,
            LocalDate determinedDate,
            Amount determinedAmount,
            LocalDate deliveryDate)
            implements ReservationSalesCase {
        @Override
        public String status() {
            return "reservation_delivered";
        }
    }
}
