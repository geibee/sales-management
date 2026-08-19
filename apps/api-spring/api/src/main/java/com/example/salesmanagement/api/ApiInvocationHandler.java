package com.example.salesmanagement.api;

import com.example.salesmanagement.application.CodeMasterQueries;
import com.example.salesmanagement.application.ExternalPricingGateway;
import com.example.salesmanagement.application.LotQueries;
import com.example.salesmanagement.application.LotUseCases;
import com.example.salesmanagement.application.SalesCaseStore;
import com.example.salesmanagement.application.SaveResult;
import com.example.salesmanagement.application.VersionedLot;
import com.example.salesmanagement.contracts.model.AuthConfigResponse;
import com.example.salesmanagement.contracts.model.AvailableLotsResponse;
import com.example.salesmanagement.contracts.model.CancelManufacturingCompletionRequest;
import com.example.salesmanagement.contracts.model.CodeMasterItem;
import com.example.salesmanagement.contracts.model.CodeMastersResponse;
import com.example.salesmanagement.contracts.model.CompleteLotShippingRequest;
import com.example.salesmanagement.contracts.model.CompleteManufacturingRequest;
import com.example.salesmanagement.contracts.model.ConfirmReservationRequest;
import com.example.salesmanagement.contracts.model.ConsignmentResult;
import com.example.salesmanagement.contracts.model.ConsignmentSalesCaseDetail;
import com.example.salesmanagement.contracts.model.ConsignmentStatusResponse;
import com.example.salesmanagement.contracts.model.Consignor;
import com.example.salesmanagement.contracts.model.CreateLotRequest;
import com.example.salesmanagement.contracts.model.CreateLotResponse;
import com.example.salesmanagement.contracts.model.CreateReservationPriceRequest;
import com.example.salesmanagement.contracts.model.CreateSalesCaseRequest;
import com.example.salesmanagement.contracts.model.CreateSalesContractRequest;
import com.example.salesmanagement.contracts.model.CreatedSalesCaseResponse;
import com.example.salesmanagement.contracts.model.DeliverReservationRequest;
import com.example.salesmanagement.contracts.model.DepartmentItem;
import com.example.salesmanagement.contracts.model.DesignateConsignmentRequest;
import com.example.salesmanagement.contracts.model.DirectAppraisal;
import com.example.salesmanagement.contracts.model.DirectContract;
import com.example.salesmanagement.contracts.model.DirectSalesCaseDetail;
import com.example.salesmanagement.contracts.model.EditCaseLotsRequest;
import com.example.salesmanagement.contracts.model.InstructItemConversionRequest;
import com.example.salesmanagement.contracts.model.InstructLotShippingRequest;
import com.example.salesmanagement.contracts.model.InstructSalesCaseShippingRequest;
import com.example.salesmanagement.contracts.model.LotDetailResponse;
import com.example.salesmanagement.contracts.model.LotResponse;
import com.example.salesmanagement.contracts.model.LotStatus;
import com.example.salesmanagement.contracts.model.LotSummary;
import com.example.salesmanagement.contracts.model.LotsListResponse;
import com.example.salesmanagement.contracts.model.PriceCheckResponse;
import com.example.salesmanagement.contracts.model.RegisterConsignmentResultRequest;
import com.example.salesmanagement.contracts.model.ReservationDelivery;
import com.example.salesmanagement.contracts.model.ReservationDetermination;
import com.example.salesmanagement.contracts.model.ReservationPrice;
import com.example.salesmanagement.contracts.model.ReservationSalesCaseDetail;
import com.example.salesmanagement.contracts.model.ReservationStatusResponse;
import com.example.salesmanagement.contracts.model.SalesCaseDetailResponse;
import com.example.salesmanagement.contracts.model.SalesCaseSummary;
import com.example.salesmanagement.contracts.model.SalesCaseType;
import com.example.salesmanagement.contracts.model.SalesCasesListResponse;
import com.example.salesmanagement.contracts.model.SectionItem;
import com.example.salesmanagement.contracts.model.ShippingCompletion;
import com.example.salesmanagement.contracts.model.ShippingInstruction;
import com.example.salesmanagement.contracts.model.UpdateSalesAppraisalRequest;
import com.example.salesmanagement.domain.ConversionDestinationInfo;
import com.example.salesmanagement.domain.ConversionInstructedLot;
import com.example.salesmanagement.domain.Count;
import com.example.salesmanagement.domain.DomainError;
import com.example.salesmanagement.domain.InventoryLot;
import com.example.salesmanagement.domain.ItemCategory;
import com.example.salesmanagement.domain.LotCommon;
import com.example.salesmanagement.domain.LotDetail;
import com.example.salesmanagement.domain.LotNumber;
import com.example.salesmanagement.domain.ManufacturedLot;
import com.example.salesmanagement.domain.ManufacturingLot;
import com.example.salesmanagement.domain.NonEmptyList;
import com.example.salesmanagement.domain.Quantity;
import com.example.salesmanagement.domain.SalesCaseNumber;
import com.example.salesmanagement.domain.ShippedLot;
import com.example.salesmanagement.domain.ShippingInstructedLot;
import com.example.salesmanagement.infrastructure.JdbcLotRepository;
import com.fasterxml.jackson.annotation.JsonInclude;
import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.nio.charset.Charset;
import java.time.Clock;
import java.time.LocalDate;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ConcurrentMap;
import org.springframework.core.env.Environment;
import org.springframework.core.io.ByteArrayResource;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.JdbcTemplate;

public final class ApiInvocationHandler implements InvocationHandler {
    private static final Charset WINDOWS_31J = Charset.forName("windows-31j");

    private final JdbcLotRepository lotRepository;
    private final LotQueries lotQueries;
    private final LotUseCases lotUseCases;
    private final SalesCaseStore salesCases;
    private final ExternalPricingGateway externalPricing;
    private final CodeMasterQueries codeMasterQueries;
    private final Clock clock;
    private final Environment environment;
    private final JdbcTemplate healthJdbc;
    private final ConcurrentMap<String, LotResponse> lotCache = new ConcurrentHashMap<>();

    public ApiInvocationHandler(
            JdbcLotRepository lotRepository,
            LotQueries lotQueries,
            LotUseCases lotUseCases,
            SalesCaseStore salesCases,
            ExternalPricingGateway externalPricing,
            CodeMasterQueries codeMasterQueries,
            Clock clock,
            Environment environment,
            JdbcTemplate healthJdbc) {
        this.lotRepository = lotRepository;
        this.lotQueries = lotQueries;
        this.lotUseCases = lotUseCases;
        this.salesCases = salesCases;
        this.externalPricing = externalPricing;
        this.codeMasterQueries = codeMasterQueries;
        this.clock = clock;
        this.environment = environment;
        this.healthJdbc = healthJdbc;
    }

    @Override
    public Object invoke(Object proxy, Method method, Object[] arguments) {
        if (method.getDeclaringClass() == Object.class) {
            return objectMethod(proxy, method, arguments);
        }
        return dispatch(method.getName(), arguments == null ? new Object[0] : arguments);
    }

    public Object dispatch(String operation, Object[] args) {
        return switch (operation) {
            case "healthCheck" -> healthCheck();
            case "getAuthConfig" -> authConfig();
            case "createLot" -> createLot((CreateLotRequest) args[0]);
            case "getLot" -> getLot((String) args[0]);
            case "listLots" -> listLots((String) args[0], (Integer) args[1], (Integer) args[2]);
            case "listAvailableLots" -> listAvailableLots((String) args[0]);
            case "exportLotsCsv" -> exportLotsCsv((String) args[0], (String) args[1]);
            case "getCodeMasters" -> getCodeMasters();
            case "completeManufacturing" ->
                completeManufacturing((String) args[0], (CompleteManufacturingRequest) args[1]);
            case "instructLotShipping" -> instructLotShipping((String) args[0], (InstructLotShippingRequest) args[1]);
            case "completeLotShipping" -> completeLotShipping((String) args[0], (CompleteLotShippingRequest) args[1]);
            case "cancelManufacturingCompletion" ->
                cancelManufacturingCompletion((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "instructItemConversion" ->
                instructItemConversion((String) args[0], (InstructItemConversionRequest) args[1]);
            case "cancelItemConversionInstruction" ->
                cancelItemConversionInstruction((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "createSalesCase" -> createSalesCase((CreateSalesCaseRequest) args[0]);
            case "listSalesCases" ->
                listSalesCases((String) args[0], (String) args[1], (Integer) args[2], (Integer) args[3]);
            case "getSalesCase" -> getSalesCase((String) args[0]);
            case "editCaseLots" -> editCaseLots((String) args[0], (EditCaseLotsRequest) args[1]);
            case "deleteSalesCase" -> deleteSalesCase((String) args[0]);
            case "createSalesAppraisal" ->
                saveAppraisal((String) args[0], (UpdateSalesAppraisalRequest) args[1], "before_appraisal");
            case "updateSalesAppraisal" ->
                saveAppraisal((String) args[0], (UpdateSalesAppraisalRequest) args[1], "appraised");
            case "deleteSalesAppraisal" ->
                deleteAppraisal((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "createSalesContract" -> saveContract((String) args[0], (CreateSalesContractRequest) args[1]);
            case "deleteSalesContract" ->
                deleteContract((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "instructSalesCaseShipping" ->
                instructSalesCaseShipping((String) args[0], (InstructSalesCaseShippingRequest) args[1]);
            case "cancelSalesCaseShippingInstruction" ->
                cancelSalesCaseShippingInstruction((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "completeSalesCaseShipping" ->
                completeSalesCaseShipping((String) args[0], (CompleteLotShippingRequest) args[1]);
            case "createReservationPrice" ->
                createReservationPrice((String) args[0], (CreateReservationPriceRequest) args[1]);
            case "confirmReservation" -> confirmReservation((String) args[0], (ConfirmReservationRequest) args[1]);
            case "cancelReservationConfirmation" ->
                cancelReservation((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "deliverReservation" -> deliverReservation((String) args[0], (DeliverReservationRequest) args[1]);
            case "designateConsignment" ->
                designateConsignment((String) args[0], (DesignateConsignmentRequest) args[1]);
            case "cancelConsignmentDesignation" ->
                cancelConsignment((String) args[0], (CancelManufacturingCompletionRequest) args[1]);
            case "registerConsignmentResult" ->
                saveConsignmentResult((String) args[0], (RegisterConsignmentResultRequest) args[1]);
            case "externalPriceCheck" -> externalPriceCheck((String) args[0]);
            default -> throw new IllegalStateException("Unknown OpenAPI operation: " + operation);
        };
    }

    private Object objectMethod(Object proxy, Method method, Object[] args) {
        return switch (method.getName()) {
            case "toString" -> "DefaultApi proxy";
            case "hashCode" -> System.identityHashCode(proxy);
            case "equals" -> proxy == args[0];
            default -> throw new IllegalStateException(method.getName());
        };
    }

    private ResponseEntity<AuthConfigResponse> authConfig() {
        boolean enabled = environment.getProperty("sales-management.authentication.enabled", Boolean.class, false);
        if (!enabled) {
            return ResponseEntity.ok(new DisabledAuthConfigResponse());
        }
        var response = new AuthConfigResponse(enabled);
        response.authority(environment.getProperty("sales-management.authentication.authority"));
        response.audience(environment.getProperty("sales-management.authentication.audience"));
        return ResponseEntity.ok(response);
    }

    @JsonInclude(JsonInclude.Include.NON_NULL)
    private static final class DisabledAuthConfigResponse extends AuthConfigResponse {
        private DisabledAuthConfigResponse() {
            super(false);
        }
    }

    private ResponseEntity<Map<String, Object>> healthCheck() {
        boolean healthy;
        try {
            healthy = healthJdbc == null
                    || Integer.valueOf(1).equals(healthJdbc.queryForObject("SELECT 1", Integer.class));
        } catch (RuntimeException exception) {
            healthy = false;
        }
        String postgresql = healthy ? "UP" : "DOWN";
        var body =
                Map.<String, Object>of("status", postgresql, "checks", Map.of("postgresql", postgresql, "self", "UP"));
        return healthy
                ? ResponseEntity.ok(body)
                : ResponseEntity.status(HttpStatus.SERVICE_UNAVAILABLE).body(body);
    }

    private ResponseEntity<PriceCheckResponse> externalPriceCheck(String lotId) {
        lotNumber(lotId);
        var result = externalPricing.fetch(lotId);
        if (result.isSuccess()) {
            var quote = result.value().orElseThrow();
            return ResponseEntity.ok(
                    new PriceCheckResponse(quote.basePrice(), quote.source()).adjustmentRate(quote.adjustmentRate()));
        }
        throw switch (result.error().orElseThrow()) {
            case ExternalPricingGateway.ExternalPricingError.CircuitOpen _ ->
                problem(HttpStatus.SERVICE_UNAVAILABLE, "Circuit breaker is OPEN for external-pricing-api");
            case ExternalPricingGateway.ExternalPricingError.Timeout timeout ->
                problem(HttpStatus.BAD_GATEWAY, "Request timed out after " + timeout.timeoutMilliseconds() + "ms");
            case ExternalPricingGateway.ExternalPricingError.UpstreamStatus status ->
                problem(HttpStatus.BAD_GATEWAY, "Upstream returned status " + status.status());
            case ExternalPricingGateway.ExternalPricingError.Malformed malformed ->
                problem(HttpStatus.BAD_GATEWAY, malformed.detail());
        };
    }

    private ResponseEntity<CreateLotResponse> createLot(CreateLotRequest request) {
        var number = request.getLotNumber();
        var details = requireNoNullElements(request.getDetails(), "details").stream()
                .map(detail -> new LotDetail(
                        ItemCategory.parse(detail.getItemCategory().toString()).orElseThrow(),
                        Optional.ofNullable(detail.getPremiumCategory()),
                        detail.getProductCategoryCode(),
                        detail.getLengthSpecLower(),
                        detail.getThicknessSpecLower(),
                        detail.getThicknessSpecUpper(),
                        detail.getQualityGrade(),
                        Count.create(detail.getCount()).value().orElseThrow(),
                        Quantity.create(detail.getQuantity()).value().orElseThrow(),
                        Optional.ofNullable(detail.getInspectionResultCategory())))
                .toList();
        var common = new LotCommon(
                new LotNumber(number.getYear(), number.getLocation(), number.getSeq()),
                request.getDivisionCode(),
                request.getDepartmentCode(),
                request.getSectionCode(),
                request.getProcessCategory(),
                request.getInspectionCategory(),
                request.getManufacturingCategory(),
                NonEmptyList.from(details).orElseThrow());
        SaveResult result = lotRepository.insert(new ManufacturingLot(common), "system");
        return switch (result) {
            case SaveResult.Saved(var lot) ->
                ResponseEntity.ok(new CreateLotResponse(
                        status(lot.value()), lot.value().common().lotNumber().toString(), lot.version()));
            case SaveResult.Duplicate _ -> throw problem(HttpStatus.CONFLICT, "Lot already exists");
            case SaveResult.Conflict _ -> throw problem(HttpStatus.CONFLICT, "Lot version conflict");
        };
    }

    private ResponseEntity<LotResponse> getLot(String id) {
        LotResponse cached = lotCache.get(id);
        if (cached != null) {
            return ResponseEntity.ok().header("X-Cache", "HIT").body(cached);
        }
        var number = lotNumber(id);
        LotResponse response = lotResponse(
                lotRepository.find(number).orElseThrow(() -> problem(HttpStatus.NOT_FOUND, "Lot not found: " + id)));
        lotCache.put(id, response);
        return ResponseEntity.ok().header("X-Cache", "MISS").body(response);
    }

    private ResponseEntity<LotsListResponse> listLots(String status, Integer limit, Integer offset) {
        int actualLimit = limit == null ? 50 : limit;
        int actualOffset = offset == null ? 0 : offset;
        var page = lotQueries.list(Optional.ofNullable(status), actualLimit, actualOffset);
        return ResponseEntity.ok(new LotsListResponse(
                page.items().stream().map(ApiInvocationHandler::lotSummary).toList(),
                page.total(),
                page.limit(),
                page.offset()));
    }

    private ResponseEntity<AvailableLotsResponse> listAvailableLots(String excludedSalesCase) {
        var items = lotQueries.listAvailable(Optional.ofNullable(excludedSalesCase)).stream()
                .map(ApiInvocationHandler::lotSummary)
                .toList();
        return ResponseEntity.ok(new AvailableLotsResponse(items, items.size()));
    }

    private ResponseEntity<ByteArrayResource> exportLotsCsv(String format, String status) {
        if (!"csv".equals(format)) {
            throw new IllegalArgumentException("format must be: csv");
        }
        var items = lotQueries.list(Optional.ofNullable(status), 200, 0).items();
        var csv = new StringBuilder(csvLine("ロット番号", "事業部", "状態", "製造完了日"));
        for (var item : items) {
            csv.append(csvLine(
                    item.value().common().lotNumber().toString(),
                    Integer.toString(item.value().common().divisionCode()),
                    item.value().status(),
                    manufacturedDate(item.value()).map(LocalDate::toString).orElse("")));
        }
        var resource = new ByteArrayResource(csv.toString().getBytes(WINDOWS_31J));
        String filename = "lots_" + LocalDate.now(clock).toString().replace("-", "") + ".csv";
        return ResponseEntity.ok()
                .header(HttpHeaders.CONTENT_DISPOSITION, "attachment; filename=\"" + filename + "\"")
                .contentType(MediaType.parseMediaType("text/csv;charset=windows-31j"))
                .body(resource);
    }

    private static String csvLine(String... fields) {
        return java.util.Arrays.stream(fields)
                        .map(ApiInvocationHandler::csvEscape)
                        .collect(java.util.stream.Collectors.joining(","))
                + "\n";
    }

    private static String csvEscape(String value) {
        String safe = value == null ? "" : value;
        if (!safe.isEmpty() && "=+-@\t\r".indexOf(safe.charAt(0)) >= 0) {
            safe = "'" + safe;
        }
        return "\"" + safe.replace("\"", "\"\"") + "\"";
    }

    private ResponseEntity<CodeMastersResponse> getCodeMasters() {
        var masters = codeMasterQueries.loadAll();
        return ResponseEntity.ok(new CodeMastersResponse(
                masters.divisions().stream()
                        .map(item -> new CodeMasterItem(item.code(), item.name()))
                        .toList(),
                masters.departments().stream()
                        .map(item -> new DepartmentItem(item.code(), item.name(), item.divisionCode()))
                        .toList(),
                masters.sections().stream()
                        .map(item -> new SectionItem(item.code(), item.name(), item.departmentCode()))
                        .toList(),
                masters.processCategories().stream()
                        .map(item -> new CodeMasterItem(item.code(), item.name()))
                        .toList(),
                masters.inspectionCategories().stream()
                        .map(item -> new CodeMasterItem(item.code(), item.name()))
                        .toList(),
                masters.manufacturingCategories().stream()
                        .map(item -> new CodeMasterItem(item.code(), item.name()))
                        .toList()));
    }

    private ResponseEntity<LotResponse> completeManufacturing(String id, CompleteManufacturingRequest request) {
        return lotResult(lotUseCases.completeManufacturing(lotNumber(id), request.getDate(), request.getVersion()));
    }

    private ResponseEntity<LotResponse> instructLotShipping(String id, InstructLotShippingRequest request) {
        return lotResult(lotUseCases.instructShipping(lotNumber(id), request.getDeadline(), request.getVersion()));
    }

    private ResponseEntity<LotResponse> completeLotShipping(String id, CompleteLotShippingRequest request) {
        return lotResult(lotUseCases.completeShipping(lotNumber(id), request.getDate(), request.getVersion()));
    }

    private ResponseEntity<LotResponse> cancelManufacturingCompletion(
            String id, CancelManufacturingCompletionRequest request) {
        return lotResult(lotUseCases.cancelManufacturingCompletion(lotNumber(id), request.getVersion()));
    }

    private ResponseEntity<LotResponse> instructItemConversion(String id, InstructItemConversionRequest request) {
        return lotResult(lotUseCases.instructItemConversion(
                lotNumber(id), new ConversionDestinationInfo(request.getDestinationItem()), request.getVersion()));
    }

    private ResponseEntity<LotResponse> cancelItemConversionInstruction(
            String id, CancelManufacturingCompletionRequest request) {
        return lotResult(lotUseCases.cancelItemConversionInstruction(lotNumber(id), request.getVersion()));
    }

    private ResponseEntity<LotResponse> lotResult(
            com.example.salesmanagement.domain.Result<VersionedLot, DomainError> result) {
        if (result.isSuccess()) {
            VersionedLot versioned = result.value().orElseThrow();
            lotCache.remove(versioned.value().common().lotNumber().toString());
            return ResponseEntity.ok(lotResponse(versioned));
        }
        throw switch (result.error().orElseThrow()) {
            case DomainError.NotFound error -> problem(HttpStatus.NOT_FOUND, error.resource() + " not found");
            case DomainError.OptimisticLockConflict _ ->
                new ApiProblemException(HttpStatus.CONFLICT, "optimistic-lock-conflict", "Version conflict");
            case DomainError.InvalidStateTransition error ->
                new ApiProblemException(HttpStatus.BAD_REQUEST, "invalid-state-transition", error.detail());
            case DomainError.ValidationFailed error ->
                new ApiProblemException(
                        HttpStatus.BAD_REQUEST,
                        "validation-error",
                        error.errors().toString());
            case DomainError.UnexpectedFailure error ->
                new ApiProblemException(HttpStatus.INTERNAL_SERVER_ERROR, "internal-error", error.detail());
        };
    }

    private ResponseEntity<CreatedSalesCaseResponse> createSalesCase(CreateSalesCaseRequest request) {
        var lotNumbers = requireNoNullElements(request.getLots(), "lots").stream()
                .map(ApiInvocationHandler::lotNumber)
                .toList();
        for (var number : lotNumbers) {
            var lot = lotRepository
                    .find(number)
                    .orElseThrow(() -> problem(HttpStatus.NOT_FOUND, "Lot not found: " + number));
            if (!(lot.value() instanceof ManufacturedLot)) {
                throw problem(HttpStatus.BAD_REQUEST, "Lot is not in manufactured state: " + number);
            }
        }
        String caseType =
                request.getCaseType() == null ? "direct" : request.getCaseType().toString();
        var header = salesCases.create(caseType, request.getDivisionCode(), request.getSalesDate(), lotNumbers);
        return ResponseEntity.ok(created(header));
    }

    private ResponseEntity<SalesCasesListResponse> listSalesCases(
            String status, String caseType, Integer limit, Integer offset) {
        int actualLimit = limit == null ? 50 : limit;
        int actualOffset = offset == null ? 0 : offset;
        if (status != null && status.indexOf('\0') >= 0) {
            return ResponseEntity.ok(new SalesCasesListResponse(List.of(), 0, actualLimit, actualOffset));
        }
        var page =
                salesCases.list(Optional.ofNullable(status), Optional.ofNullable(caseType), actualLimit, actualOffset);
        var items = page.items().stream()
                .map(header -> new SalesCaseSummary(
                                format(header.number()), SalesCaseType.fromValue(header.caseType()), header.status())
                        .salesDate(header.salesDate())
                        .divisionCode(header.divisionCode()))
                .toList();
        return ResponseEntity.ok(new SalesCasesListResponse(items, page.total(), page.limit(), page.offset()));
    }

    private ResponseEntity<SalesCaseDetailResponse> getSalesCase(String id) {
        var header = findCase(id);
        List<String> lots = header.lots().stream().map(LotNumber::toString).toList();
        SalesCaseDetailResponse detail =
                switch (header.caseType()) {
                    case "direct" ->
                        directDetail((SalesCaseStore.DirectDetails) header.details())
                                .salesCaseNumber(format(header.number()))
                                .caseType(DirectSalesCaseDetail.CaseTypeEnum.DIRECT)
                                .status(header.status())
                                .lots(lots)
                                .divisionCode(header.divisionCode())
                                .salesDate(header.salesDate())
                                .version(header.version());
                    case "reservation" ->
                        reservationDetail((SalesCaseStore.ReservationDetails) header.details())
                                .salesCaseNumber(format(header.number()))
                                .caseType(ReservationSalesCaseDetail.CaseTypeEnum.RESERVATION)
                                .status(header.status())
                                .lots(lots)
                                .divisionCode(header.divisionCode())
                                .salesDate(header.salesDate())
                                .version(header.version());
                    case "consignment" ->
                        consignmentDetail((SalesCaseStore.ConsignmentDetails) header.details())
                                .salesCaseNumber(format(header.number()))
                                .caseType(ConsignmentSalesCaseDetail.CaseTypeEnum.CONSIGNMENT)
                                .status(header.status())
                                .lots(lots)
                                .divisionCode(header.divisionCode())
                                .salesDate(header.salesDate())
                                .version(header.version());
                    default -> throw new IllegalStateException(header.caseType());
                };
        return ResponseEntity.ok(detail);
    }

    private static DirectSalesCaseDetail directDetail(SalesCaseStore.DirectDetails details) {
        return new DirectSalesCaseDetail()
                .appraisal(details.appraisal()
                        .map(value -> new DirectAppraisal(
                                value.type(),
                                value.appraisalDate(),
                                value.deliveryDate(),
                                value.salesMarket(),
                                value.taxExcludedEstimatedTotal()))
                        .orElse(null))
                .contract(details.contract()
                        .map(value -> new DirectContract(
                                value.contractDate(),
                                value.person(),
                                value.customerNumber(),
                                value.taxExcludedContractAmount(),
                                value.consumptionTax()))
                        .orElse(null))
                .shippingInstruction(details.shippingInstructionDate()
                        .map(ShippingInstruction::new)
                        .orElse(null))
                .shippingCompletion(details.shippingCompletionDate()
                        .map(ShippingCompletion::new)
                        .orElse(null));
    }

    private static ReservationSalesCaseDetail reservationDetail(SalesCaseStore.ReservationDetails details) {
        var response = new ReservationSalesCaseDetail();
        details.reservationPrice().ifPresent(value -> {
            response.reservationPrice(
                    new ReservationPrice(value.appraisalDate(), value.reservedLotInfo(), value.reservedAmount()));
            response.determination(value.determinedDate()
                    .flatMap(date -> value.determinedAmount().map(amount -> new ReservationDetermination(date, amount)))
                    .orElse(null));
            response.delivery(
                    value.deliveredDate().map(ReservationDelivery::new).orElse(null));
        });
        return response;
    }

    private static ConsignmentSalesCaseDetail consignmentDetail(SalesCaseStore.ConsignmentDetails details) {
        return new ConsignmentSalesCaseDetail()
                .consignor(details.consignor()
                        .map(value -> new Consignor(value.name(), value.code(), value.designatedDate()))
                        .orElse(null))
                .result(details.result()
                        .map(value -> new ConsignmentResult(value.resultDate(), value.resultAmount()))
                        .orElse(null));
    }

    private ResponseEntity<CreatedSalesCaseResponse> editCaseLots(String id, EditCaseLotsRequest request) {
        var number = caseNumber(id);
        var lots = requireNoNullElements(request.getLots(), "lots").stream()
                .map(ApiInvocationHandler::lotNumber)
                .toList();
        var updated = salesCases
                .editLots(number, request.getVersion(), lots)
                .orElseThrow(() -> problem(HttpStatus.CONFLICT, "Version or state conflict"));
        return ResponseEntity.ok(created(updated));
    }

    private ResponseEntity<Void> deleteSalesCase(String id) {
        findCase(id);
        boolean deleted = salesCases.delete(
                caseNumber(id), List.of("before_appraisal", "before_reservation", "before_consignment"));
        if (!deleted) {
            throw problem(HttpStatus.BAD_REQUEST, "Sales case cannot be deleted in its current state");
        }
        return ResponseEntity.noContent().build();
    }

    private ResponseEntity<CreatedSalesCaseResponse> saveAppraisal(
            String id, UpdateSalesAppraisalRequest request, String expectedStatus) {
        var current = findCaseOfType(id, "direct");
        var appraisal = new SalesCaseStore.DirectAppraisal(
                request.getType().toString(),
                request.getAppraisalDate(),
                request.getDeliveryDate(),
                request.getSalesMarket(),
                request.getBaseUnitPriceDate(),
                request.getPeriodAdjustmentRateDate(),
                request.getCounterpartyAdjustmentRateDate(),
                request.getTaxExcludedEstimatedTotal(),
                Optional.ofNullable(request.getCustomerContractNumber()),
                Optional.ofNullable(request.getContractAdjustmentRate()),
                request.getLotAppraisals().stream()
                        .map(lot -> new SalesCaseStore.LotAppraisal(
                                lotNumber(lot.getLotNumber()),
                                lot.getDetailAppraisals().stream()
                                        .map(detail -> new SalesCaseStore.DetailAppraisal(
                                                detail.getDetailIndex(),
                                                detail.getBaseUnitPrice(),
                                                detail.getPeriodAdjustmentRate(),
                                                detail.getCounterpartyAdjustmentRate(),
                                                Optional.ofNullable(detail.getExceptionalPeriodAdjustmentRate())))
                                        .toList()))
                        .toList());
        var updated = salesCases
                .saveAppraisal(current.number(), request.getVersion(), expectedStatus, appraisal)
                .orElseThrow(() -> conflict());
        return ResponseEntity.ok(created(updated));
    }

    private ResponseEntity<Void> deleteAppraisal(String id, CancelManufacturingCompletionRequest request) {
        var current = findCaseOfType(id, "direct");
        if (salesCases.deleteAppraisal(current.number(), request.getVersion()).isEmpty()) {
            throw conflict();
        }
        return ResponseEntity.noContent().build();
    }

    private ResponseEntity<CreatedSalesCaseResponse> saveContract(String id, CreateSalesContractRequest request) {
        var current = findCaseOfType(id, "direct");
        var buyer = request.getBuyer();
        var contract = new SalesCaseStore.DirectContract(
                request.getContractDate(),
                request.getPerson(),
                buyer.getCustomerNumber(),
                Optional.ofNullable(buyer.getAgentName()),
                request.getSalesType(),
                request.getItem(),
                request.getDeliveryMethod(),
                optionalString(request.getPaymentDeferralCondition()),
                request.getSalesMethod(),
                optionalString(request.getUsage()),
                Optional.ofNullable(request.getPaymentDeferralAmount()),
                request.getTaxExcludedContractAmount(),
                request.getConsumptionTax(),
                request.getTaxExcludedPaymentAmount(),
                request.getPaymentConsumptionTax());
        var updated = salesCases
                .saveContract(current.number(), request.getVersion(), contract)
                .orElseThrow(() -> conflict());
        return ResponseEntity.ok(created(updated));
    }

    private static Optional<String> optionalString(String value) {
        return Optional.ofNullable(value).filter(candidate -> !candidate.isEmpty());
    }

    private ResponseEntity<Void> deleteContract(String id, CancelManufacturingCompletionRequest request) {
        var current = findCaseOfType(id, "direct");
        if (salesCases.deleteContract(current.number(), request.getVersion()).isEmpty()) {
            throw conflict();
        }
        return ResponseEntity.noContent().build();
    }

    private ResponseEntity<ReservationStatusResponse> createReservationPrice(
            String id, CreateReservationPriceRequest request) {
        var current = findCaseOfType(id, "reservation");
        var value = new SalesCaseStore.ReservationPrice(
                request.getAppraisalDate(),
                request.getReservedLotInfo(),
                request.getReservedAmount(),
                Optional.empty(),
                Optional.empty(),
                Optional.empty());
        var updated = salesCases
                .saveReservationPrice(current.number(), request.getVersion(), value)
                .orElseThrow(() -> conflict());
        return reservationResponse(updated);
    }

    private ResponseEntity<ReservationStatusResponse> confirmReservation(String id, ConfirmReservationRequest request) {
        var current = findCaseOfType(id, "reservation");
        var updated = salesCases
                .confirmReservation(
                        current.number(),
                        request.getVersion(),
                        request.getDeterminedDate(),
                        request.getDeterminedAmount())
                .orElseThrow(() -> conflict());
        return reservationResponse(updated);
    }

    private ResponseEntity<ReservationStatusResponse> cancelReservation(
            String id, CancelManufacturingCompletionRequest request) {
        var current = findCaseOfType(id, "reservation");
        var updated = salesCases
                .cancelReservation(current.number(), request.getVersion())
                .orElseThrow(() -> conflict());
        return reservationResponse(updated);
    }

    private ResponseEntity<ReservationStatusResponse> deliverReservation(String id, DeliverReservationRequest request) {
        var current = findCaseOfType(id, "reservation");
        var updated = salesCases
                .deliverReservation(current.number(), request.getVersion(), request.getDeliveryDate())
                .orElseThrow(() -> conflict());
        return reservationResponse(updated);
    }

    private ResponseEntity<ConsignmentStatusResponse> designateConsignment(
            String id, DesignateConsignmentRequest request) {
        var current = findCaseOfType(id, "consignment");
        var consignor = new SalesCaseStore.Consignor(
                request.getConsignorName(), request.getConsignorCode(), request.getDesignatedDate());
        var updated = salesCases
                .designateConsignment(current.number(), request.getVersion(), consignor)
                .orElseThrow(() -> conflict());
        return consignmentResponse(updated);
    }

    private ResponseEntity<ConsignmentStatusResponse> cancelConsignment(
            String id, CancelManufacturingCompletionRequest request) {
        var current = findCaseOfType(id, "consignment");
        var updated = salesCases
                .cancelConsignment(current.number(), request.getVersion())
                .orElseThrow(() -> conflict());
        return consignmentResponse(updated);
    }

    private ResponseEntity<ConsignmentStatusResponse> saveConsignmentResult(
            String id, RegisterConsignmentResultRequest request) {
        var current = findCaseOfType(id, "consignment");
        var result = new SalesCaseStore.ConsignmentResult(request.getResultDate(), request.getResultAmount());
        var updated = salesCases
                .saveConsignmentResult(current.number(), request.getVersion(), result)
                .orElseThrow(() -> conflict());
        return consignmentResponse(updated);
    }

    private ResponseEntity<CreatedSalesCaseResponse> instructSalesCaseShipping(
            String id, InstructSalesCaseShippingRequest request) {
        var updated = salesCases
                .changeStatusWithDate(
                        caseNumber(id),
                        request.getVersion(),
                        "contracted",
                        "shipping_instructed",
                        SalesCaseStore.DateField.SHIPPING_INSTRUCTION,
                        Optional.of(request.getDate()))
                .orElseThrow(() -> problem(HttpStatus.CONFLICT, "Version or state conflict"));
        return ResponseEntity.ok(created(updated));
    }

    private ResponseEntity<Void> cancelSalesCaseShippingInstruction(
            String id, CancelManufacturingCompletionRequest request) {
        if (salesCases
                .changeStatusWithDate(
                        caseNumber(id),
                        request.getVersion(),
                        "shipping_instructed",
                        "contracted",
                        SalesCaseStore.DateField.SHIPPING_INSTRUCTION,
                        Optional.empty())
                .isEmpty()) {
            throw problem(HttpStatus.CONFLICT, "Version or state conflict");
        }
        return ResponseEntity.noContent().build();
    }

    private ResponseEntity<CreatedSalesCaseResponse> completeSalesCaseShipping(
            String id, CompleteLotShippingRequest request) {
        var updated = salesCases
                .changeStatusWithDate(
                        caseNumber(id),
                        request.getVersion(),
                        "shipping_instructed",
                        "shipping_completed",
                        SalesCaseStore.DateField.SHIPPING_COMPLETION,
                        Optional.of(request.getDate()))
                .orElseThrow(() -> problem(HttpStatus.CONFLICT, "Version or state conflict"));
        return ResponseEntity.ok(created(updated));
    }

    private static ResponseEntity<ReservationStatusResponse> reservationResponse(SalesCaseStore.Header header) {
        return ResponseEntity.ok(new ReservationStatusResponse(header.status(), header.version()));
    }

    private static ResponseEntity<ConsignmentStatusResponse> consignmentResponse(SalesCaseStore.Header header) {
        return ResponseEntity.ok(new ConsignmentStatusResponse(header.status(), header.version()));
    }

    private SalesCaseStore.Header findCase(String id) {
        return salesCases
                .find(caseNumber(id))
                .orElseThrow(() -> problem(HttpStatus.NOT_FOUND, "Sales case not found: " + id));
    }

    private SalesCaseStore.Header findCaseOfType(String id, String caseType) {
        var header = findCase(id);
        if (!caseType.equals(header.caseType())) {
            throw problem(HttpStatus.BAD_REQUEST, "Sales case type must be " + caseType);
        }
        return header;
    }

    private static CreatedSalesCaseResponse created(SalesCaseStore.Header header) {
        return new CreatedSalesCaseResponse(format(header.number()), header.status(), header.version());
    }

    private LotResponse lotResponse(VersionedLot versioned) {
        var lot = versioned.value();
        var common = lot.common();
        var masters = codeMasterQueries.loadAll();
        return new LotResponse()
                .status(status(lot))
                .lotNumber(common.lotNumber().toString())
                .version(versioned.version())
                .manufacturingCompletedDate(manufacturedDate(lot).orElse(null))
                .shippingDeadlineDate(
                        lot instanceof ShippingInstructedLot value
                                ? value.shippingDeadlineDate()
                                : lot instanceof ShippedLot value ? value.shippingDeadlineDate() : null)
                .shippedDate(lot instanceof ShippedLot value ? value.shippedDate() : null)
                .destinationItem(
                        lot instanceof ConversionInstructedLot value
                                ? value.destinationInfo().destinationItem()
                                : null)
                .division(codeName(common.divisionCode(), masters.divisionName(common.divisionCode())))
                .department(codeName(common.departmentCode(), masters.departmentName(common.departmentCode())))
                .section(codeName(common.sectionCode(), masters.sectionName(common.sectionCode())))
                .processCategory(
                        codeName(common.processCategory(), masters.processCategoryName(common.processCategory())))
                .inspectionCategory(codeName(
                        common.inspectionCategory(), masters.inspectionCategoryName(common.inspectionCategory())))
                .manufacturingCategory(codeName(
                        common.manufacturingCategory(),
                        masters.manufacturingCategoryName(common.manufacturingCategory())))
                .details(common.details().values().stream()
                        .map(ApiInvocationHandler::lotDetail)
                        .toList());
    }

    private static com.example.salesmanagement.contracts.model.CodeName codeName(int code, Optional<String> name) {
        return new com.example.salesmanagement.contracts.model.CodeName(code).name(name.orElse(null));
    }

    private static LotDetailResponse lotDetail(LotDetail detail) {
        return new LotDetailResponse(
                        LotDetailResponse.ItemCategoryEnum.fromValue(
                                detail.itemCategory().wireValue()),
                        detail.productCategoryCode(),
                        detail.lengthSpecLower(),
                        detail.thicknessSpecLower(),
                        detail.thicknessSpecUpper(),
                        detail.qualityGrade(),
                        detail.count().value(),
                        detail.quantity().value())
                .premiumCategory(detail.premiumCategory().orElse(null))
                .inspectionResultCategory(detail.inspectionResultCategory().orElse(null));
    }

    private static LotSummary lotSummary(VersionedLot versioned) {
        return new LotSummary(
                        status(versioned.value()),
                        versioned.value().common().lotNumber().toString(),
                        versioned.version())
                .manufacturingCompletedDate(manufacturedDate(versioned.value()).orElse(null));
    }

    private static Optional<LocalDate> manufacturedDate(InventoryLot lot) {
        return switch (lot) {
            case ManufacturingLot _ -> Optional.empty();
            case ManufacturedLot value -> Optional.of(value.manufacturingCompletedDate());
            case ShippingInstructedLot value -> Optional.of(value.manufacturingCompletedDate());
            case ShippedLot value -> Optional.of(value.manufacturingCompletedDate());
            case ConversionInstructedLot value -> Optional.of(value.manufacturingCompletedDate());
        };
    }

    private static LotStatus status(InventoryLot lot) {
        return LotStatus.fromValue(lot.status());
    }

    private static LotNumber lotNumber(String value) {
        return LotNumber.parse(value)
                .value()
                .orElseThrow(() -> problem(HttpStatus.BAD_REQUEST, "Invalid lot number: " + value));
    }

    private static <T> List<T> requireNoNullElements(List<T> values, String field) {
        if (values.stream().anyMatch(java.util.Objects::isNull)) {
            throw ApiProblemException.validation(
                    List.of(new com.example.salesmanagement.contracts.model.ProblemDetailsErrorsInner()
                            .field(field)
                            .message("array items must not be null")));
        }
        return values;
    }

    private static SalesCaseNumber caseNumber(String value) {
        return SalesCaseNumber.parse(value)
                .orElseThrow(() -> problem(HttpStatus.BAD_REQUEST, "Invalid sales case number: " + value));
    }

    private static String format(SalesCaseNumber number) {
        return "%d-%02d-%03d".formatted(number.year(), number.month(), number.sequence());
    }

    private static ApiProblemException problem(HttpStatus status, String detail) {
        return new ApiProblemException(status, detail);
    }

    private static ApiProblemException conflict() {
        return new ApiProblemException(HttpStatus.CONFLICT, "optimistic-lock-conflict", "Version or state conflict");
    }
}
