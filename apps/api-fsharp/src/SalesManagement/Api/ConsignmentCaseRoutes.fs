module SalesManagement.Api.ConsignmentCaseRoutes

open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.ReservationCaseTypes
open SalesManagement.Domain.Errors
open SalesManagement.Infrastructure
open SalesManagement.Api.SalesCaseDtos
open SalesManagement.Api.ConsignmentCaseDtos
open SalesManagement.Api.ProblemDetails

let private mapDomainErrorToResponse (err: DomainError) : HttpHandler = toResponse "SalesCase" err

let private requireVersion (v: System.Nullable<int>) : Result<int, HttpHandler> =
    if v.HasValue then
        Ok v.Value
    else
        Error(badRequest "version is required")

let private bindBody<'T> (ctx: HttpContext) : System.Threading.Tasks.Task<Result<'T, HttpHandler>> = task {
    try
        let! dto = ctx.BindJsonAsync<'T>()
        return Ok dto
    with ex ->
        return Error(badRequest (sprintf "Invalid body: %s" ex.Message))
}

let private resolveConsignmentHeader
    (conn: NpgsqlConnection)
    (id: string)
    (expectedStatus: string)
    (errorCode: string)
    : Result<SalesCaseRepository.SalesCaseHeader, HttpHandler> =
    match trySalesCaseNumber id with
    | None -> Error(badRequest "Invalid sales case id")
    | Some n ->
        match SalesCaseRepository.tryFindHeader conn n with
        | None -> Error(notFound (sprintf "Sales case not found: %s" id))
        | Some header when header.CaseType <> "consignment" -> Error(badRequest "CaseTypeNotConsignment")
        | Some header when header.Status <> expectedStatus -> Error(badRequest errorCode)
        | Some header -> Ok header

let private runWithConsignmentHeader
    (connectionString: string)
    (id: string)
    (expectedStatus: string)
    (errorCode: string)
    (action: NpgsqlConnection -> SalesCaseRepository.SalesCaseHeader -> HttpHandler)
    : HttpHandler =
    fun next ctx -> task {
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()

        let handler =
            match resolveConsignmentHeader conn id expectedStatus errorCode with
            | Error h -> h
            | Ok header -> action conn header

        return! handler next ctx
    }

let private executeCancelDesignation
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (expectedVersion: int)
    : HttpHandler =
    match ConsignmentRepository.deleteConsignmentInfo conn header.SalesCaseNumber expectedVersion with
    | Error e -> mapDomainErrorToResponse e
    | Ok newVersion ->
        json
            { status = "before_consignment"
              version = newVersion }

let private executeDesignate
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (dto: DesignateConsignmentDto)
    (expectedVersion: int)
    : HttpHandler =
    match parseDate dto.designatedDate with
    | None -> badRequest "designatedDate must be ISO date"
    | Some date ->
        let info: ConsignorInfo =
            { ConsignorName = dto.consignorName
              ConsignorCode = dto.consignorCode
              DesignatedDate = date }

        match ConsignmentRepository.insertConsignmentInfo conn header.SalesCaseNumber info expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { status = "consignment_designated"
                  version = newVersion }

let private designateHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<DesignateConsignmentDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header = executeDesignate conn header dto v

                return!
                    runWithConsignmentHeader
                        connectionString
                        id
                        "before_consignment"
                        "CaseNotBeforeConsignment"
                        action
                        next
                        ctx
    }

let private cancelDesignationHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<ConsignmentVersionOnlyDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header = executeCancelDesignation conn header v

                return!
                    runWithConsignmentHeader
                        connectionString
                        id
                        "consignment_designated"
                        "CaseNotConsignmentDesignated"
                        action
                        next
                        ctx
    }

let private executeResult
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (dto: ConsignmentResultDto)
    (expectedVersion: int)
    : HttpHandler =
    match parseDate dto.resultDate, Amount.tryCreate dto.resultAmount with
    | None, _ -> badRequest "resultDate must be ISO date"
    | _, Error e -> badRequest e
    | Some date, Ok amount ->
        let result: ConsignmentResult =
            { ResultDate = date
              ResultAmount = amount }

        match ConsignmentRepository.insertConsignmentResult conn header.SalesCaseNumber result expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { status = "consignment_result_entered"
                  version = newVersion }

let private resultHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<ConsignmentResultDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header = executeResult conn header dto v

                return!
                    runWithConsignmentHeader
                        connectionString
                        id
                        "consignment_designated"
                        "CaseNotConsignmentDesignated"
                        action
                        next
                        ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose
        [ POST
          >=> routef "/sales-cases/%s/consignment/designate" (designateHandler connectionString)
          DELETE
          >=> routef "/sales-cases/%s/consignment/designation" (cancelDesignationHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/consignment/result" (resultHandler connectionString) ]
