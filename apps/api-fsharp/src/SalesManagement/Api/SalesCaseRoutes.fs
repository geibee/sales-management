module SalesManagement.Api.SalesCaseRoutes

open System
open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.SalesCaseWorkflows
open SalesManagement.Domain.Errors
open SalesManagement.Infrastructure
open SalesManagement.Api.SalesCaseDtos
open SalesManagement.Api.ProblemDetails

let private requireVersion (v: System.Nullable<int>) : Result<int, HttpHandler> =
    if v.HasValue then
        Ok v.Value
    else
        Error(badRequest "version is required")

let private requireVersionFromBody<'T>
    (ctx: HttpContext)
    (extract: 'T -> System.Nullable<int>)
    : System.Threading.Tasks.Task<Result<int * 'T, HttpHandler>> =
    task {
        try
            let! dto = ctx.BindJsonAsync<'T>()

            match requireVersion (extract dto) with
            | Error e -> return Error e
            | Ok v -> return Ok(v, dto)
        with ex ->
            return Error(badRequest (sprintf "Invalid body: %s" ex.Message))
    }

let private mapDomainErrorToResponse (err: DomainError) : HttpHandler = toResponse "SalesCase" err

let private respondCaseChange (n: SalesCaseNumber) (status: string) (result: Result<int, DomainError>) : HttpHandler =
    match result with
    | Error e -> mapDomainErrorToResponse e
    | Ok newVersion ->
        json
            { salesCaseNumber = formatSalesCaseNumber n
              status = status
              version = newVersion }

/// DB 接続を開いて HttpHandler を組み立てる共通ヘルパ。
/// ハンドラ本体のネストを浅く保つための足場 (FSharpLint FL0015 対応)。
let private runWithConn (connectionString: string) (build: NpgsqlConnection -> HttpHandler) : HttpHandler =
    fun next ctx -> task {
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        return! build conn next ctx
    }

/// ヘッダ取得 + status ガードの共通前段。
let private requireCaseStatus
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (id: string)
    (requiredStatus: string)
    (statusError: string)
    : Result<unit, HttpHandler> =
    match SalesCaseRepository.tryFindHeader conn n with
    | None -> Error(notFound (sprintf "Sales case not found: %s" id))
    | Some header when header.Status <> requiredStatus -> Error(badRequest statusError)
    | Some _ -> Ok()

let private resolveAppraisedAppraisalNumber
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (id: string)
    : Result<AppraisalNumber, HttpHandler> =
    match SalesCaseRepository.tryFindHeader conn n with
    | None -> Error(notFound (sprintf "Sales case not found: %s" id))
    | Some header when header.Status <> "appraised" -> Error(badRequest "CaseNotAppraised")
    | Some header ->
        match header.AppraisalNumber with
        | None -> Error(badRequest "Appraisal not present")
        | Some appraisalNumber -> Ok appraisalNumber

let private lookupManufacturedLot (conn: NpgsqlConnection) (id: string) : Result<ManufacturedLot, HttpHandler> =
    match LotNumber.tryParse id with
    | None -> Error(badRequest (sprintf "Invalid lot number %s" id))
    | Some lotNumber ->
        match LotRepository.load conn lotNumber with
        | Error e -> Error(badRequest e)
        | Ok None ->
            // 参照先リソースの不存在は 400 でなく 404 (openapi.yaml の 404 と対)
            Error(notFound (sprintf "Lot %s not found" (LotNumber.toString lotNumber)))
        | Ok(Some lot) ->
            match requireManufacturedLot lot with
            | Ok m -> Ok m
            | Error(LotNotManufactured id) -> Error(badRequest (sprintf "Lot %s is not in manufactured state" id))

let private fetchManufacturedLots
    (conn: NpgsqlConnection)
    (lotIds: string[])
    : Result<ManufacturedLot list, HttpHandler> =
    let lookups = lotIds |> Array.toList |> List.map (lookupManufacturedLot conn)

    let folded =
        lookups
        |> List.fold
            (fun acc r ->
                match acc, r with
                | Error e, _ -> Error e
                | Ok _, Error e -> Error e
                | Ok xs, Ok d -> Ok(d :: xs))
            (Ok [])

    folded |> Result.map List.rev

// ロットが（いずれかの）販売案件に既に割り当てられているか。
let private isAssignedToAnyCase (conn: NpgsqlConnection) (lot: ManufacturedLot) : bool =
    let lotNumber = (InventoryLot.common (Manufactured lot)).LotNumber
    SalesCaseRepository.findCaseNumbersByLot conn lotNumber |> List.isEmpty |> not

let private resolveCaseType (raw: string) : Result<string * string, string> =
    let normalized = if String.IsNullOrEmpty raw then "direct" else raw

    match normalized with
    | "direct" -> Ok("direct", "before_appraisal")
    | "reservation" -> Ok("reservation", "before_reservation")
    | "consignment" -> Ok("consignment", "before_consignment")
    | other -> Error(sprintf "Unknown caseType: %s" other)

let private createSalesCaseCore
    (caseType: string)
    (initialStatus: string)
    (salesDate: DateOnly)
    (dto: CreateSalesCaseDto)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match fetchManufacturedLots conn dto.lots with
    | Error h -> h
    | Ok lots when lots |> List.exists (isAssignedToAnyCase conn) ->
        badRequest "One or more lots are already assigned to a sales case"
    | Ok lots ->
        let nextSeq =
            SalesCaseRepository.nextSalesCaseSeq conn salesDate.Year salesDate.Month

        let caseNumber: SalesCaseNumber =
            { Year = salesDate.Year
              Month = salesDate.Month
              Seq = nextSeq }

        match createSalesCaseCommon lots caseNumber (DivisionCode dto.divisionCode) salesDate with
        | Error NoManufacturedLots -> badRequest "NoManufacturedLots"
        | Ok common ->
            SalesCaseRepository.insertCommon conn caseType initialStatus common

            json
                { salesCaseNumber = formatSalesCaseNumber caseNumber
                  status = initialStatus
                  version = 1 }

let private bindCreateSalesCase (ctx: HttpContext) : Threading.Tasks.Task<Result<CreateSalesCaseDto, string>> = task {
    try
        let! dto = ctx.BindJsonAsync<CreateSalesCaseDto>()

        if isNull (box dto) then
            return Error "request body is required"
        elif isNull (box dto.lots) then
            return Error "lots is required"
        else
            return Ok dto
    with ex ->
        // 型不一致 (caseType: {} 等) の bind 失敗は 400 (500 にしない。Schemathesis 検出)
        return Error(sprintf "Invalid body: %s" ex.Message)
}

let private createSalesCaseHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        let! bound = bindCreateSalesCase ctx

        match bound with
        | Error message -> return! badRequest message next ctx
        | Ok dto ->

            match parseDate dto.salesDate with
            | None -> return! badRequest "salesDate must be ISO date" next ctx
            | Some salesDate ->
                match resolveCaseType dto.caseType with
                | Error e -> return! badRequest e next ctx
                | Ok(caseType, initialStatus) ->
                    return!
                        runWithConn connectionString (createSalesCaseCore caseType initialStatus salesDate dto) next ctx
    }

let private deleteSalesCaseHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()

            match SalesCaseRepository.tryFindHeader conn n with
            | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
            | Some header when header.Status <> "before_appraisal" ->
                return! badRequest "CaseNotBeforeAppraisal" next ctx
            | Some _ ->
                SalesCaseRepository.deleteCase conn n
                return! Successful.NO_CONTENT next ctx
    }

let private buildAndInsertAppraisal
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (dto: CreateAppraisalDto)
    (lots: ManufacturedLot list)
    (expectedVersion: int)
    : HttpHandler =
    let appraisalNumber: AppraisalNumber =
        { Year = n.Year
          Month = n.Month
          Seq = AppraisalRepository.nextAppraisalSeq conn n.Year n.Month }

    match buildPriceAppraisal dto appraisalNumber lots with
    | Error e -> badRequest e
    | Ok appraisal ->
        match AppraisalRepository.insertAppraisal conn n appraisalNumber appraisal expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { salesCaseNumber = formatSalesCaseNumber n
                  status = "appraised"
                  version = newVersion }

let private createAppraisalCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (dto: CreateAppraisalDto)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match requireCaseStatus conn n id "before_appraisal" "CaseNotBeforeAppraisal" with
    | Error h -> h
    | Ok() ->
        match SalesCaseRepository.loadCaseLots conn n with
        | Error e -> badRequest e
        | Ok lots -> buildAndInsertAppraisal conn n dto lots v

let private createAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateAppraisalDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) -> return! runWithConn connectionString (createAppraisalCore id n v dto) next ctx
    }

let private buildAndUpdateAppraisal
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (dto: CreateAppraisalDto)
    (lots: ManufacturedLot list)
    (expectedVersion: int)
    : HttpHandler =
    match buildPriceAppraisal dto appraisalNumber lots with
    | Error e -> badRequest e
    | Ok appraisal ->
        match AppraisalRepository.updateAppraisal conn n appraisalNumber appraisal expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { salesCaseNumber = formatSalesCaseNumber n
                  status = "appraised"
                  version = newVersion }

let private updateAppraisalCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (dto: CreateAppraisalDto)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match resolveAppraisedAppraisalNumber conn n id with
    | Error h -> h
    | Ok appraisalNumber ->
        match SalesCaseRepository.loadCaseLots conn n with
        | Error e -> badRequest e
        | Ok lots -> buildAndUpdateAppraisal conn n appraisalNumber dto lots v

let private updateAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateAppraisalDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) -> return! runWithConn connectionString (updateAppraisalCore id n v dto) next ctx
    }

let private deleteAppraisalCore (id: string) (n: SalesCaseNumber) (v: int) (conn: NpgsqlConnection) : HttpHandler =
    match resolveAppraisedAppraisalNumber conn n id with
    | Error h -> h
    | Ok appraisalNumber ->
        match AppraisalRepository.deleteAppraisal conn n appraisalNumber v with
        | Error e -> mapDomainErrorToResponse e
        | Ok _ -> Successful.NO_CONTENT

let private deleteAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) -> return! runWithConn connectionString (deleteAppraisalCore id n v) next ctx
    }

let private executeContractInsert
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (appraisalNumber: AppraisalNumber)
    (dto: CreateContractDto)
    (expectedVersion: int)
    : HttpHandler =
    let contractNumber: ContractNumber =
        { Year = n.Year
          Month = n.Month
          Seq = SalesCaseRepository.nextContractSeq conn n.Year n.Month }

    match buildSalesContract dto contractNumber appraisalNumber with
    | Error e -> badRequest e
    | Ok contract ->
        let result =
            SalesCaseRepository.insertContract conn n appraisalNumber contractNumber contract expectedVersion

        respondCaseChange n "contracted" result

let private createContractCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (dto: CreateContractDto)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match resolveAppraisedAppraisalNumber conn n id with
    | Error h -> h
    | Ok appraisalNumber -> executeContractInsert conn n appraisalNumber dto v

let private createContractHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateContractDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) -> return! runWithConn connectionString (createContractCore id n v dto) next ctx
    }

let private resolveContractedContractNumber
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (id: string)
    : Result<ContractNumber, HttpHandler> =
    match SalesCaseRepository.tryFindHeader conn n with
    | None -> Error(notFound (sprintf "Sales case not found: %s" id))
    | Some header when header.Status <> "contracted" -> Error(badRequest "CaseNotContracted")
    | Some header ->
        match header.ContractNumber with
        | None -> Error(badRequest "Contract not present")
        | Some contractNumber -> Ok contractNumber

let private deleteContractCore (id: string) (n: SalesCaseNumber) (v: int) (conn: NpgsqlConnection) : HttpHandler =
    match resolveContractedContractNumber conn n id with
    | Error h -> h
    | Ok contractNumber ->
        match SalesCaseRepository.deleteContract conn n contractNumber v with
        | Error e -> mapDomainErrorToResponse e
        | Ok _ -> Successful.NO_CONTENT

let private deleteContractHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) -> return! runWithConn connectionString (deleteContractCore id n v) next ctx
    }

let private instructShippingCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (date: DateOnly)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match requireCaseStatus conn n id "contracted" "CaseNotContracted" with
    | Error h -> h
    | Ok() ->
        let result = SalesCaseRepository.setShippingInstruction conn n date v
        respondCaseChange n "shipping_instructed" result

let private instructShippingHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<DateOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) ->
                match parseDate dto.date with
                | None -> return! badRequest "date must be ISO date" next ctx
                | Some date -> return! runWithConn connectionString (instructShippingCore id n v date) next ctx
    }

let private completeShippingCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (date: DateOnly)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match requireCaseStatus conn n id "shipping_instructed" "CaseNotShippingInstructed" with
    | Error h -> h
    | Ok() ->
        let result = SalesCaseRepository.setShippingCompletion conn n date v
        respondCaseChange n "shipping_completed" result

let private completeShippingHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<DateOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) ->
                match parseDate dto.date with
                | None -> return! badRequest "date must be ISO date" next ctx
                | Some date -> return! runWithConn connectionString (completeShippingCore id n v date) next ctx
    }

let private cancelShippingInstructionCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match requireCaseStatus conn n id "shipping_instructed" "CaseNotShippingInstructed" with
    | Error h -> h
    | Ok() ->
        match SalesCaseRepository.clearShippingInstruction conn n v with
        | Error e -> mapDomainErrorToResponse e
        | Ok _ -> Successful.NO_CONTENT

let private cancelShippingInstructionHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) -> return! runWithConn connectionString (cancelShippingInstructionCore id n v) next ctx
    }


// ロット組み合わせの修正。価格/査定登録前の direct・consignment のみ許可（予約は対象外）。
let private caseLotsEditable (header: SalesCaseRepository.SalesCaseHeader) : bool =
    match header.CaseType, header.Status with
    | "direct", "before_appraisal" -> true
    | "consignment", "before_consignment" -> true
    | _ -> false

/// 指定案件以外の販売案件に既に割り当てられているか。
let private isAssignedToAnotherCase (conn: NpgsqlConnection) (n: SalesCaseNumber) (lot: ManufacturedLot) : bool =
    let lotNumber = (InventoryLot.common (Manufactured lot)).LotNumber

    SalesCaseRepository.findCaseNumbersByLot conn lotNumber
    |> List.exists (fun c -> c <> n)

let private replaceCaseLotsIfUnassigned
    (conn: NpgsqlConnection)
    (n: SalesCaseNumber)
    (header: SalesCaseRepository.SalesCaseHeader)
    (v: int)
    (lots: ManufacturedLot list)
    : HttpHandler =
    if lots |> List.exists (isAssignedToAnotherCase conn n) then
        badRequest "One or more lots are already assigned to another sales case"
    else
        let result = SalesCaseRepository.replaceCaseLots conn n lots header.Status v
        respondCaseChange n header.Status result

let private editCaseLotsCore
    (id: string)
    (n: SalesCaseNumber)
    (v: int)
    (dto: EditCaseLotsDto)
    (conn: NpgsqlConnection)
    : HttpHandler =
    match SalesCaseRepository.tryFindHeader conn n with
    | None -> notFound (sprintf "Sales case not found: %s" id)
    | Some header when not (caseLotsEditable header) ->
        badRequest "Lots can only be edited for a direct case before appraisal or a consignment case before designation"
    | Some header ->
        match fetchManufacturedLots conn dto.lots with
        | Error h -> h
        | Ok lots when List.isEmpty lots -> badRequest "At least one lot is required"
        | Ok lots -> replaceCaseLotsIfUnassigned conn n header v lots

let private editCaseLotsHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<EditCaseLotsDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) -> return! runWithConn connectionString (editCaseLotsCore id n v dto) next ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose
        [ POST >=> route "/sales-cases" >=> createSalesCaseHandler connectionString
          PUT >=> routef "/sales-cases/%s/lots" (editCaseLotsHandler connectionString)
          GET
          >=> route "/sales-cases"
          >=> SalesCaseListRoutes.listSalesCasesHandler connectionString
          GET
          >=> routef "/sales-cases/%s" (SalesCaseDetailRoutes.getSalesCaseHandler connectionString)
          DELETE >=> routef "/sales-cases/%s" (deleteSalesCaseHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/appraisals" (createAppraisalHandler connectionString)
          PUT
          >=> routef "/sales-cases/%s/appraisals" (updateAppraisalHandler connectionString)
          DELETE
          >=> routef "/sales-cases/%s/appraisals" (deleteAppraisalHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/contracts" (createContractHandler connectionString)
          DELETE
          >=> routef "/sales-cases/%s/contracts" (deleteContractHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/shipping-instruction" (instructShippingHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/shipping-completion" (completeShippingHandler connectionString)
          DELETE
          >=> routef "/sales-cases/%s/shipping-instruction" (cancelShippingInstructionHandler connectionString) ]
