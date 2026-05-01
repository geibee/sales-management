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

let private fetchManufacturedLots (conn: NpgsqlConnection) (lotIds: string[]) : Result<ManufacturedLot list, string> =
    let lookups =
        lotIds
        |> Array.toList
        |> List.map (fun id ->
            match LotNumber.tryParse id with
            | None -> Error(sprintf "Invalid lot number %s" id)
            | Some lotNumber ->
                match LotRepository.load conn lotNumber with
                | Error e -> Error e
                | Ok None -> Error(sprintf "Lot %s not found" (LotNumber.toString lotNumber))
                | Ok(Some(Manufactured lot)) -> Ok lot
                | Ok(Some _) -> Error(sprintf "Lot %s is not in manufactured state" (LotNumber.toString lotNumber)))

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

let private resolveCaseType (raw: string) : Result<string * string, string> =
    let normalized = if String.IsNullOrEmpty raw then "direct" else raw

    match normalized with
    | "direct" -> Ok("direct", "before_appraisal")
    | "reservation" -> Ok("reservation", "before_reservation")
    | "consignment" -> Ok("consignment", "before_consignment")
    | other -> Error(sprintf "Unknown caseType: %s" other)

let private createSalesCaseHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        let! dto = ctx.BindJsonAsync<CreateSalesCaseDto>()

        match parseDate dto.salesDate with
        | None -> return! badRequest "salesDate must be ISO date" next ctx
        | Some salesDate ->
            match resolveCaseType dto.caseType with
            | Error e -> return! badRequest e next ctx
            | Ok(caseType, initialStatus) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match fetchManufacturedLots conn dto.lots with
                | Error e -> return! badRequest e next ctx
                | Ok lots ->
                    let nextSeq =
                        SalesCaseRepository.nextSalesCaseSeq conn salesDate.Year salesDate.Month

                    let caseNumber: SalesCaseNumber =
                        { Year = salesDate.Year
                          Month = salesDate.Month
                          Seq = nextSeq }

                    match createSalesCaseCommon lots caseNumber (DivisionCode dto.divisionCode) salesDate with
                    | Error NoManufacturedLots -> return! badRequest "NoManufacturedLots" next ctx
                    | Ok common ->
                        SalesCaseRepository.insertCommon conn caseType initialStatus common

                        let response =
                            { salesCaseNumber = formatSalesCaseNumber caseNumber
                              status = initialStatus
                              version = 1 }

                        return! json response next ctx
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

let private createAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateAppraisalDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match SalesCaseRepository.tryFindHeader conn n with
                | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                | Some header when header.Status <> "before_appraisal" ->
                    return! badRequest "CaseNotBeforeAppraisal" next ctx
                | Some _ ->
                    match SalesCaseRepository.loadCaseLots conn n with
                    | Error e -> return! badRequest e next ctx
                    | Ok lots -> return! buildAndInsertAppraisal conn n dto lots v next ctx
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

let private updateAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateAppraisalDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match SalesCaseRepository.tryFindHeader conn n with
                | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                | Some header when header.Status <> "appraised" -> return! badRequest "CaseNotAppraised" next ctx
                | Some header ->
                    match header.AppraisalNumber with
                    | None -> return! badRequest "Appraisal not present" next ctx
                    | Some appraisalNumber ->
                        match SalesCaseRepository.loadCaseLots conn n with
                        | Error e -> return! badRequest e next ctx
                        | Ok lots -> return! buildAndUpdateAppraisal conn n appraisalNumber dto lots v next ctx
    }

let private deleteAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match SalesCaseRepository.tryFindHeader conn n with
                | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                | Some header when header.Status <> "appraised" -> return! badRequest "CaseNotAppraised" next ctx
                | Some header ->
                    match header.AppraisalNumber with
                    | None -> return! badRequest "Appraisal not present" next ctx
                    | Some appraisalNumber ->
                        match AppraisalRepository.deleteAppraisal conn n appraisalNumber v with
                        | Error e -> return! mapDomainErrorToResponse e next ctx
                        | Ok _ -> return! Successful.NO_CONTENT next ctx
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

let private createContractHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<CreateContractDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, dto) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match resolveAppraisedAppraisalNumber conn n id with
                | Error h -> return! h next ctx
                | Ok appraisalNumber -> return! executeContractInsert conn n appraisalNumber dto v next ctx
    }

let private deleteContractHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match SalesCaseRepository.tryFindHeader conn n with
                | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                | Some header when header.Status <> "contracted" -> return! badRequest "CaseNotContracted" next ctx
                | Some header ->
                    match header.ContractNumber with
                    | None -> return! badRequest "Contract not present" next ctx
                    | Some contractNumber ->
                        match SalesCaseRepository.deleteContract conn n contractNumber v with
                        | Error e -> return! mapDomainErrorToResponse e next ctx
                        | Ok _ -> return! Successful.NO_CONTENT next ctx
    }

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
                | Some date ->
                    use conn = new NpgsqlConnection(connectionString)
                    conn.Open()

                    match SalesCaseRepository.tryFindHeader conn n with
                    | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                    | Some header when header.Status <> "contracted" -> return! badRequest "CaseNotContracted" next ctx
                    | Some _ ->
                        let result = SalesCaseRepository.setShippingInstruction conn n date v
                        return! respondCaseChange n "shipping_instructed" result next ctx
    }

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
                | Some date ->
                    use conn = new NpgsqlConnection(connectionString)
                    conn.Open()

                    match SalesCaseRepository.tryFindHeader conn n with
                    | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                    | Some header when header.Status <> "shipping_instructed" ->
                        return! badRequest "CaseNotShippingInstructed" next ctx
                    | Some _ ->
                        let result = SalesCaseRepository.setShippingCompletion conn n date v
                        return! respondCaseChange n "shipping_completed" result next ctx
    }

let private cancelShippingInstructionHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match trySalesCaseNumber id with
        | None -> return! badRequest "Invalid sales case id" next ctx
        | Some n ->
            let! r = requireVersionFromBody<VersionOnlyDto> ctx (fun d -> d.version)

            match r with
            | Error h -> return! h next ctx
            | Ok(v, _) ->
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match SalesCaseRepository.tryFindHeader conn n with
                | None -> return! notFound (sprintf "Sales case not found: %s" id) next ctx
                | Some header when header.Status <> "shipping_instructed" ->
                    return! badRequest "CaseNotShippingInstructed" next ctx
                | Some _ ->
                    match SalesCaseRepository.clearShippingInstruction conn n v with
                    | Error e -> return! mapDomainErrorToResponse e next ctx
                    | Ok _ -> return! Successful.NO_CONTENT next ctx
    }


type SalesCaseSummary =
    { salesCaseNumber: string
      divisionCode: int
      salesDate: string
      caseType: string
      status: string }

type ListSalesCasesResponse =
    { items: SalesCaseSummary[]
      total: int
      limit: int
      offset: int }

let private toSalesCaseSummary (item: SalesCaseListRepository.SalesCaseListItem) : SalesCaseSummary =
    { salesCaseNumber = formatSalesCaseNumber item.SalesCaseNumber
      divisionCode = item.DivisionCode
      salesDate = item.SalesDate.ToString("yyyy-MM-dd")
      caseType = item.CaseType
      status = item.Status }

let private tryGetIntQuery (ctx: HttpContext) (key: string) : Result<int option, string> =
    match ctx.Request.Query.TryGetValue key with
    | false, _ -> Ok None
    | true, v ->
        let s = v.ToString()

        if String.IsNullOrEmpty s then
            Ok None
        else
            match Int32.TryParse s with
            | true, n -> Ok(Some n)
            | false, _ -> Error(sprintf "%s must be an integer" key)

let private tryGetStringQuery (ctx: HttpContext) (key: string) : string option =
    match ctx.Request.Query.TryGetValue key with
    | false, _ -> None
    | true, v ->
        let s = v.ToString()
        if String.IsNullOrEmpty s then None else Some s

let listSalesCasesHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        let limitR = tryGetIntQuery ctx "limit"
        let offsetR = tryGetIntQuery ctx "offset"

        match limitR, offsetR with
        | Error msg, _ -> return! badRequest msg next ctx
        | _, Error msg -> return! badRequest msg next ctx
        | Ok limitOpt, Ok offsetOpt ->
            let limit = limitOpt |> Option.defaultValue 50
            let offset = offsetOpt |> Option.defaultValue 0

            if limit < 1 || limit > 200 then
                return! badRequest "limit must be between 1 and 200" next ctx
            elif offset < 0 then
                return! badRequest "offset must be >= 0" next ctx
            else
                let status = tryGetStringQuery ctx "status"
                let caseType = tryGetStringQuery ctx "caseType"

                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                let result =
                    SalesCaseListRepository.list
                        conn
                        { Status = status
                          CaseType = caseType
                          Limit = limit
                          Offset = offset }

                let response: ListSalesCasesResponse =
                    { items = result.Items |> List.map toSalesCaseSummary |> List.toArray
                      total = result.Total
                      limit = result.Limit
                      offset = result.Offset }

                return! json response next ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose
        [ POST >=> route "/sales-cases" >=> createSalesCaseHandler connectionString
          GET >=> route "/sales-cases" >=> listSalesCasesHandler connectionString
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
