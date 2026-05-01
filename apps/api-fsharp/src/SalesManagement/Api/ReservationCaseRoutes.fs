module SalesManagement.Api.ReservationCaseRoutes

open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.SalesCaseTypes
open SalesManagement.Domain.ReservationCaseTypes
open SalesManagement.Domain.Errors
open SalesManagement.Infrastructure
open SalesManagement.Api.SalesCaseDtos
open SalesManagement.Api.ReservationCaseDtos
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

let private resolveReservationHeader
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
        | Some header when header.CaseType <> "reservation" -> Error(badRequest "CaseTypeNotReservation")
        | Some header when header.Status <> expectedStatus -> Error(badRequest errorCode)
        | Some header -> Ok header

let private runWithReservationHeader
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
            match resolveReservationHeader conn id expectedStatus errorCode with
            | Error h -> h
            | Ok header -> action conn header

        return! handler next ctx
    }

let private executeCreateAppraisal
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (dto: CreateReservationPriceDto)
    (expectedVersion: int)
    : HttpHandler =
    match parseDate dto.appraisalDate, Amount.tryCreate dto.reservedAmount with
    | None, _ -> badRequest "appraisalDate must be ISO date"
    | _, Error e -> badRequest e
    | Some appraisalDate, Ok amount ->
        let caseNumber = header.SalesCaseNumber

        let appraisalNumber: AppraisalNumber =
            { Year = caseNumber.Year
              Month = caseNumber.Month
              Seq = ReservationPriceRepository.nextReservationPriceSeq conn caseNumber.Year caseNumber.Month }

        let common: ReservationPriceCommon =
            { AppraisalNumber = appraisalNumber
              AppraisalDate = appraisalDate
              ReservedLotInfo = dto.reservedLotInfo
              ReservedAmount = amount }

        match
            ReservationPriceRepository.insertProvisionalReservationPrice
                conn
                caseNumber
                appraisalNumber
                common
                expectedVersion
        with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { status = "reserved"
                  version = newVersion }

let private createAppraisalHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<CreateReservationPriceDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header =
                    executeCreateAppraisal conn header dto v

                return!
                    runWithReservationHeader
                        connectionString
                        id
                        "before_reservation"
                        "CaseNotBeforeReservation"
                        action
                        next
                        ctx
    }

let private executeDetermine
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (dto: ConfirmReservationDto)
    (expectedVersion: int)
    : HttpHandler =
    match parseDate dto.determinedDate, Amount.tryCreate dto.determinedAmount with
    | None, _ -> badRequest "determinedDate must be ISO date"
    | _, Error e -> badRequest e
    | Some date, Ok amount ->
        let caseNumber = header.SalesCaseNumber

        match ReservationPriceRepository.tryFindByCase conn caseNumber with
        | None -> badRequest "Reservation price not present"
        | Some row ->
            match
                ReservationPriceRepository.setConfirmed conn caseNumber row.AppraisalNumber date amount expectedVersion
            with
            | Error e -> mapDomainErrorToResponse e
            | Ok newVersion ->
                json
                    { status = "reservation_confirmed"
                      version = newVersion }

let private determineHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<ConfirmReservationDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header = executeDetermine conn header dto v

                return! runWithReservationHeader connectionString id "reserved" "CaseNotReserved" action next ctx
    }

let private executeCancelDetermination
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (expectedVersion: int)
    : HttpHandler =
    let caseNumber = header.SalesCaseNumber

    match ReservationPriceRepository.tryFindByCase conn caseNumber with
    | None -> badRequest "Reservation price not present"
    | Some row ->
        match ReservationPriceRepository.clearConfirmed conn caseNumber row.AppraisalNumber expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { status = "reserved"
                  version = newVersion }

let private cancelReservationConfirmationHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<ReservationVersionOnlyDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header =
                    executeCancelDetermination conn header v

                return!
                    runWithReservationHeader
                        connectionString
                        id
                        "reservation_confirmed"
                        "CaseNotReservationConfirmed"
                        action
                        next
                        ctx
    }

let private executeDeliver
    (conn: NpgsqlConnection)
    (header: SalesCaseRepository.SalesCaseHeader)
    (dto: DeliverReservationDto)
    (expectedVersion: int)
    : HttpHandler =
    match parseDate dto.deliveryDate with
    | None -> badRequest "deliveryDate must be ISO date"
    | Some date ->
        match ReservationPriceRepository.setDelivered conn header.SalesCaseNumber date expectedVersion with
        | Error e -> mapDomainErrorToResponse e
        | Ok newVersion ->
            json
                { status = "reservation_delivered"
                  version = newVersion }

let private deliverHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        let! r = bindBody<DeliverReservationDto> ctx

        match r with
        | Error h -> return! h next ctx
        | Ok dto ->
            match requireVersion dto.version with
            | Error h -> return! h next ctx
            | Ok v ->
                let action conn header = executeDeliver conn header dto v

                return!
                    runWithReservationHeader
                        connectionString
                        id
                        "reservation_confirmed"
                        "CaseNotReservationConfirmed"
                        action
                        next
                        ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose
        [ POST
          >=> routef "/sales-cases/%s/reservation/appraisals" (createAppraisalHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/reservation/determine" (determineHandler connectionString)
          DELETE
          >=> routef "/sales-cases/%s/reservation/determination" (cancelReservationConfirmationHandler connectionString)
          POST
          >=> routef "/sales-cases/%s/reservation/delivery" (deliverHandler connectionString) ]
