package com.example.salesmanagement.api;

import com.example.salesmanagement.contracts.api.DefaultApi;
import com.example.salesmanagement.contracts.model.AvailableLotsResponse;
import com.example.salesmanagement.contracts.model.CancelManufacturingCompletionRequest;
import com.example.salesmanagement.contracts.model.CodeMastersResponse;
import com.example.salesmanagement.contracts.model.CompleteLotShippingRequest;
import com.example.salesmanagement.contracts.model.CompleteManufacturingRequest;
import com.example.salesmanagement.contracts.model.ConfirmReservationRequest;
import com.example.salesmanagement.contracts.model.ConsignmentStatusResponse;
import com.example.salesmanagement.contracts.model.CreateLotRequest;
import com.example.salesmanagement.contracts.model.CreateLotResponse;
import com.example.salesmanagement.contracts.model.CreateReservationPriceRequest;
import com.example.salesmanagement.contracts.model.CreateSalesCaseRequest;
import com.example.salesmanagement.contracts.model.CreateSalesContractRequest;
import com.example.salesmanagement.contracts.model.CreatedSalesCaseResponse;
import com.example.salesmanagement.contracts.model.DeliverReservationRequest;
import com.example.salesmanagement.contracts.model.DesignateConsignmentRequest;
import com.example.salesmanagement.contracts.model.EditCaseLotsRequest;
import com.example.salesmanagement.contracts.model.InstructItemConversionRequest;
import com.example.salesmanagement.contracts.model.InstructLotShippingRequest;
import com.example.salesmanagement.contracts.model.InstructSalesCaseShippingRequest;
import com.example.salesmanagement.contracts.model.LotResponse;
import com.example.salesmanagement.contracts.model.LotsListResponse;
import com.example.salesmanagement.contracts.model.PriceCheckResponse;
import com.example.salesmanagement.contracts.model.RegisterConsignmentResultRequest;
import com.example.salesmanagement.contracts.model.ReservationStatusResponse;
import com.example.salesmanagement.contracts.model.SalesCaseDetailResponse;
import com.example.salesmanagement.contracts.model.SalesCasesListResponse;
import com.example.salesmanagement.contracts.model.UpdateSalesAppraisalRequest;
import org.springframework.core.io.Resource;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.RestController;

@RestController
public class SalesManagementController implements DefaultApi {
    private final ApiInvocationHandler dispatcher;

    public SalesManagementController(ApiInvocationHandler dispatcher) {
        this.dispatcher = dispatcher;
    }

    @SuppressWarnings("unchecked")
    private <T> ResponseEntity<T> call(String operation, Object... arguments) {
        return (ResponseEntity<T>) dispatcher.dispatch(operation, arguments);
    }

    @Override
    public ResponseEntity<ConsignmentStatusResponse> cancelConsignmentDesignation(
            String id, CancelManufacturingCompletionRequest request) {
        return call("cancelConsignmentDesignation", id, request);
    }

    @Override
    public ResponseEntity<LotResponse> cancelItemConversionInstruction(
            String id, CancelManufacturingCompletionRequest request) {
        return call("cancelItemConversionInstruction", id, request);
    }

    @Override
    public ResponseEntity<LotResponse> cancelManufacturingCompletion(
            String id, CancelManufacturingCompletionRequest request) {
        return call("cancelManufacturingCompletion", id, request);
    }

    @Override
    public ResponseEntity<ReservationStatusResponse> cancelReservationConfirmation(
            String id, CancelManufacturingCompletionRequest request) {
        return call("cancelReservationConfirmation", id, request);
    }

    @Override
    public ResponseEntity<Void> cancelSalesCaseShippingInstruction(
            String id, CancelManufacturingCompletionRequest request) {
        return call("cancelSalesCaseShippingInstruction", id, request);
    }

    @Override
    public ResponseEntity<LotResponse> completeLotShipping(String id, CompleteLotShippingRequest request) {
        return call("completeLotShipping", id, request);
    }

    @Override
    public ResponseEntity<LotResponse> completeManufacturing(String id, CompleteManufacturingRequest request) {
        return call("completeManufacturing", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> completeSalesCaseShipping(
            String id, CompleteLotShippingRequest request) {
        return call("completeSalesCaseShipping", id, request);
    }

    @Override
    public ResponseEntity<ReservationStatusResponse> confirmReservation(String id, ConfirmReservationRequest request) {
        return call("confirmReservation", id, request);
    }

    @Override
    public ResponseEntity<CreateLotResponse> createLot(CreateLotRequest request) {
        return call("createLot", request);
    }

    @Override
    public ResponseEntity<ReservationStatusResponse> createReservationPrice(
            String id, CreateReservationPriceRequest request) {
        return call("createReservationPrice", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> createSalesAppraisal(
            String id, UpdateSalesAppraisalRequest request) {
        return call("createSalesAppraisal", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> createSalesCase(CreateSalesCaseRequest request) {
        return call("createSalesCase", request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> createSalesContract(String id, CreateSalesContractRequest request) {
        return call("createSalesContract", id, request);
    }

    @Override
    public ResponseEntity<Void> deleteSalesAppraisal(String id, CancelManufacturingCompletionRequest request) {
        return call("deleteSalesAppraisal", id, request);
    }

    @Override
    public ResponseEntity<Void> deleteSalesCase(String id) {
        return call("deleteSalesCase", id);
    }

    @Override
    public ResponseEntity<Void> deleteSalesContract(String id, CancelManufacturingCompletionRequest request) {
        return call("deleteSalesContract", id, request);
    }

    @Override
    public ResponseEntity<ReservationStatusResponse> deliverReservation(String id, DeliverReservationRequest request) {
        return call("deliverReservation", id, request);
    }

    @Override
    public ResponseEntity<ConsignmentStatusResponse> designateConsignment(
            String id, DesignateConsignmentRequest request) {
        return call("designateConsignment", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> editCaseLots(String id, EditCaseLotsRequest request) {
        return call("editCaseLots", id, request);
    }

    @Override
    public ResponseEntity<Resource> exportLotsCsv(String format, String status) {
        return call("exportLotsCsv", format, status);
    }

    @Override
    public ResponseEntity<PriceCheckResponse> externalPriceCheck(String item) {
        return call("externalPriceCheck", item);
    }

    @Override
    public ResponseEntity<com.example.salesmanagement.contracts.model.AuthConfigResponse> getAuthConfig() {
        return call("getAuthConfig");
    }

    @Override
    public ResponseEntity<CodeMastersResponse> getCodeMasters() {
        return call("getCodeMasters");
    }

    @Override
    public ResponseEntity<LotResponse> getLot(String id) {
        return call("getLot", id);
    }

    @Override
    public ResponseEntity<SalesCaseDetailResponse> getSalesCase(String id) {
        return call("getSalesCase", id);
    }

    @Override
    public ResponseEntity<Void> healthCheck() {
        return call("healthCheck");
    }

    @Override
    public ResponseEntity<LotResponse> instructItemConversion(String id, InstructItemConversionRequest request) {
        return call("instructItemConversion", id, request);
    }

    @Override
    public ResponseEntity<LotResponse> instructLotShipping(String id, InstructLotShippingRequest request) {
        return call("instructLotShipping", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> instructSalesCaseShipping(
            String id, InstructSalesCaseShippingRequest request) {
        return call("instructSalesCaseShipping", id, request);
    }

    @Override
    public ResponseEntity<AvailableLotsResponse> listAvailableLots(String excludedSalesCase) {
        return call("listAvailableLots", excludedSalesCase);
    }

    @Override
    public ResponseEntity<LotsListResponse> listLots(String status, Integer limit, Integer offset) {
        return call("listLots", status, limit, offset);
    }

    @Override
    public ResponseEntity<SalesCasesListResponse> listSalesCases(
            String status, String caseType, Integer limit, Integer offset) {
        return call("listSalesCases", status, caseType, limit, offset);
    }

    @Override
    public ResponseEntity<ConsignmentStatusResponse> registerConsignmentResult(
            String id, RegisterConsignmentResultRequest request) {
        return call("registerConsignmentResult", id, request);
    }

    @Override
    public ResponseEntity<CreatedSalesCaseResponse> updateSalesAppraisal(
            String id, UpdateSalesAppraisalRequest request) {
        return call("updateSalesAppraisal", id, request);
    }
}
