package com.example.salesmanagement.domain;

public sealed interface ConsignmentSalesCase {
    SalesCaseCommon common();

    String status();

    record BeforeConsignment(SalesCaseCommon common) implements ConsignmentSalesCase {
        @Override
        public String status() {
            return "before_consignment";
        }
    }

    record Designated(SalesCaseCommon common, ConsignorInfo consignor) implements ConsignmentSalesCase {
        @Override
        public String status() {
            return "consignment_designated";
        }
    }

    record ResultEntered(SalesCaseCommon common, ConsignorInfo consignor, ConsignmentResult result)
            implements ConsignmentSalesCase {
        @Override
        public String status() {
            return "consignment_result_entered";
        }
    }
}
