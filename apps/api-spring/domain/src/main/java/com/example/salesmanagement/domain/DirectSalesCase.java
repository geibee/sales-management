package com.example.salesmanagement.domain;

import java.time.LocalDate;

public sealed interface DirectSalesCase {
    SalesCaseCommon common();

    String status();

    record BeforeAppraisal(SalesCaseCommon common) implements DirectSalesCase {
        @Override
        public String status() {
            return "before_appraisal";
        }
    }

    record Appraised(SalesCaseCommon common, String appraisalReference) implements DirectSalesCase {
        @Override
        public String status() {
            return "appraised";
        }
    }

    record Contracted(SalesCaseCommon common, String appraisalReference, String contractReference)
            implements DirectSalesCase {
        @Override
        public String status() {
            return "contracted";
        }
    }

    record ShippingInstructed(
            SalesCaseCommon common, String appraisalReference, String contractReference, LocalDate instructionDate)
            implements DirectSalesCase {
        @Override
        public String status() {
            return "shipping_instructed";
        }
    }

    record ShippingCompleted(
            SalesCaseCommon common,
            String appraisalReference,
            String contractReference,
            LocalDate instructionDate,
            LocalDate completedDate)
            implements DirectSalesCase {
        @Override
        public String status() {
            return "shipping_completed";
        }
    }
}
