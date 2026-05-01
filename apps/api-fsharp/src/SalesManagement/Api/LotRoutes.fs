module SalesManagement.Api.LotRoutes

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open Serilog
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.Errors
open SalesManagement.Domain.LotWorkflows
open SalesManagement.Domain.Events
open SalesManagement.Infrastructure
open SalesManagement.Api.ProblemDetails
open SalesManagement.Api.LotDtos

let private toResponseBody (lot: InventoryLot) (version: int) : LotResponse =
    let common = InventoryLot.common lot
    let formatDate (d: DateOnly) = d.ToString("yyyy-MM-dd")

    let mfg, deadline, shipped, destination =
        match lot with
        | Manufacturing _ -> None, None, None, None
        | Manufactured l -> Some(formatDate l.ManufacturingCompletedDate), None, None, None
        | ConversionInstructed l ->
            Some(formatDate l.ManufacturingCompletedDate), None, None, Some l.DestinationInfo.DestinationItem
        | ShippingInstructed l ->
            Some(formatDate l.ManufacturingCompletedDate), Some(formatDate l.ShippingDeadlineDate), None, None
        | Shipped l ->
            Some(formatDate l.ManufacturingCompletedDate),
            Some(formatDate l.ShippingDeadlineDate),
            Some(formatDate l.ShippedDate),
            None

    { status = InventoryLot.statusString lot
      lotNumber = LotNumber.toString common.LotNumber
      manufacturingCompletedDate = mfg
      shippingDeadlineDate = deadline
      shippedDate = shipped
      destinationItem = destination
      version = version }

let private parseLotId (id: string) : Result<LotNumber, DomainError> =
    match LotNumber.tryParse id with
    | Some n -> Ok n
    | None ->
        Error(
            ValidationFailed
                [ { Field = "lotNumber"
                    Message = sprintf "Invalid lot id: %s" id } ]
        )

let private respondError (err: DomainError) : HttpHandler = toResponse "Lot" err

// cacheKey takes a parsed LotNumber so 2026-A-1 / 2026-A-001 share the entry.
let cacheKey (lotNumber: LotNumber) : string =
    sprintf "lot:%s" (LotNumber.toString lotNumber)

let private invalidateLotCacheByNumber (ctx: HttpContext) (lotNumber: LotNumber) : unit =
    let cache = ctx.RequestServices.GetRequiredService<IMemoryCache>()
    let key = cacheKey lotNumber
    cache.Remove(key)
    Log.Debug("Cache INVALIDATE key={Key}", key)

let private invalidateLotCache (ctx: HttpContext) (id: string) : unit =
    match LotNumber.tryParse id with
    | Some n -> invalidateLotCacheByNumber ctx n
    | None -> ()

let private duplicateLotResponse (lotNumber: string) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        ctx.Response.StatusCode <- 409
        ctx.Response.ContentType <- "application/problem+json"

        let body =
            sprintf
                """{"type":"https://errors.example.com/duplicate-resource","title":"Duplicate resource","status":409,"detail":"Lot %s already exists"}"""
                lotNumber

        do! ctx.Response.WriteAsync body
        return Some ctx
    }

let private internalLotErrorResponse: HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        ctx.Response.StatusCode <- 500
        ctx.Response.ContentType <- "application/problem+json"

        do!
            ctx.Response.WriteAsync(
                """{"type":"https://errors.example.com/internal-error","title":"Internal server error","status":500,"detail":"Lot creation failed"}"""
            )

        return Some ctx
    }

let private requireVersion (version: System.Nullable<int>) : Result<int, DomainError> =
    if version.HasValue then
        Ok version.Value
    else
        Error(
            ValidationFailed
                [ { Field = "version"
                    Message = "version is required" } ]
        )

let private requireDate (raw: string) : Result<DateOnly, DomainError> =
    match DateOnly.TryParse(if isNull raw then "" else raw) with
    | true, d -> Ok d
    | false, _ ->
        Error(
            ValidationFailed
                [ { Field = "date"
                    Message = "must be ISO date" } ]
        )

let private requireDeadline (raw: string) : Result<DateOnly, DomainError> =
    match DateOnly.TryParse(if isNull raw then "" else raw) with
    | true, d -> Ok d
    | false, _ ->
        Error(
            ValidationFailed
                [ { Field = "deadline"
                    Message = "must be ISO date" } ]
        )

let private requireDestinationItem (s: string) : Result<string, DomainError> =
    if String.IsNullOrEmpty s then
        Error(
            ValidationFailed
                [ { Field = "destinationItem"
                    Message = "must not be empty" } ]
        )
    else
        Ok s

// Donald wraps Npgsql.PostgresException inside DbExecutionException;
// walk the inner chain to find the SqlState 23505 (unique violation).
let private isUniqueViolation (ex: exn) : bool =
    let rec walk (e: exn) =
        match e with
        | null -> false
        | :? PostgresException as pex -> pex.SqlState = "23505"
        | _ -> walk e.InnerException

    walk ex

let private tryInsertLot
    (connectionString: string)
    (userId: string)
    (lot: InventoryLot)
    (lotNumberStr: string)
    : Result<unit, int> =
    try
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        LotRepository.insert conn userId lot
        Ok()
    with ex ->
        if isUniqueViolation ex then
            Log.Information("Lot creation rejected as duplicate {LotNumber}", lotNumberStr)
            Error 409
        else
            Log.Error(ex, "Lot creation failed for {LotNumber}", lotNumberStr)
            Error 500

let private respondCreateLot (lot: InventoryLot) (lotNumberStr: string) (outcome: Result<unit, int>) : HttpHandler =
    match outcome with
    | Ok() ->
        let response: CreateLotResponse =
            { status = InventoryLot.statusString lot
              lotNumber = lotNumberStr
              version = 1 }

        json response
    | Error 409 -> duplicateLotResponse lotNumberStr
    | Error _ -> internalLotErrorResponse

let private bindCreateRequest (ctx: HttpContext) : Task<Result<CreateLotRequest, exn>> = task {
    try
        let! dto = ctx.BindJsonAsync<CreateLotRequest>()
        return Ok dto
    with ex ->
        return Error ex
}

let private parseCreateLot (ctx: HttpContext) : Task<Result<InventoryLot, DomainError>> = task {
    let! parsed = bindCreateRequest ctx

    match parsed with
    | Error ex -> return Error(ValidationFailed [ { Field = "body"; Message = ex.Message } ])
    | Ok dto ->
        match validateCreateLotRequest dto with
        | Error errors -> return Error(ValidationFailed errors)
        | Ok lot -> return Ok lot
}

let createLotHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        let! validated = parseCreateLot ctx

        match validated with
        | Error err -> return! respondError err next ctx
        | Ok lot ->
            let lotNumberStr = LotNumber.toString (InventoryLot.common lot).LotNumber
            let userId = SalesManagement.Api.Auth.getUserIdOrSystem ctx
            let outcome = tryInsertLot connectionString userId lot lotNumberStr
            return! respondCreateLot lot lotNumberStr outcome next ctx
    }

let private completeManufacturingTransition (date: DateOnly) (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | Manufacturing m -> Ok(Manufactured(completeManufacturing date m))
    | _ -> Error(InvalidStateTransition "Lot is not in manufacturing state")

let private instructShippingTransition (deadline: DateOnly) (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | Manufactured m -> Ok(ShippingInstructed(instructShipping deadline m))
    | _ -> Error(InvalidStateTransition "Lot is not in manufactured state")

let private completeShippingTransition (date: DateOnly) (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | ShippingInstructed s -> Ok(Shipped(completeShipping date s))
    | _ -> Error(InvalidStateTransition "Lot is not in shipping-instructed state")

let private cancelManufacturingTransition (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | Manufactured m -> Ok(Manufacturing(cancelManufacturingCompletion m))
    | _ -> Error(InvalidStateTransition "Lot is not in manufactured state")

let private instructItemConversionTransition
    (destinationItem: string)
    (lot: InventoryLot)
    : Result<InventoryLot, DomainError> =
    match lot with
    | Manufactured m ->
        let info: ConversionDestinationInfo = { DestinationItem = destinationItem }
        Ok(ConversionInstructed(instructItemConversion info m))
    | _ -> Error(InvalidStateTransition "Lot is not in manufactured state")

let private cancelItemConversionInstructionTransition (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | ConversionInstructed c -> Ok(Manufactured(cancelItemConversionInstruction c))
    | _ -> Error(InvalidStateTransition "Lot is not in conversion-instructed state")

let private loadCurrent
    (conn: NpgsqlConnection)
    (id: string)
    (lotNumber: LotNumber)
    : Result<InventoryLot, DomainError> =
    match LotRepository.loadWithVersion conn lotNumber with
    | Error e -> Error(InternalError e)
    | Ok None -> Error(NotFound("Lot", id))
    | Ok(Some(current, _)) -> Ok current

let private executeTransition
    (connectionString: string)
    (userId: string)
    (id: string)
    (expectedVersion: int)
    (transition: InventoryLot -> Result<InventoryLot, DomainError>)
    : Result<InventoryLot * int, DomainError> =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    parseLotId id
    |> Result.bind (loadCurrent conn id)
    |> Result.bind transition
    |> Result.bind (fun updated ->
        LotRepository.updateWithVersion conn userId updated expectedVersion
        |> Result.map (fun newVersion -> updated, newVersion))

let private executeTransitionWithEvent
    (connectionString: string)
    (userId: string)
    (id: string)
    (expectedVersion: int)
    (transition: InventoryLot -> Result<InventoryLot, DomainError>)
    (makeEvent: InventoryLot -> DomainEvent option)
    : Result<InventoryLot * int, DomainError> =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()

    let loaded =
        parseLotId id |> Result.bind (loadCurrent conn id) |> Result.bind transition

    match loaded with
    | Error e -> Error e
    | Ok updated ->
        use tx = conn.BeginTransaction()

        match LotRepository.updateWithVersionTx tx userId updated expectedVersion with
        | Error e ->
            tx.Rollback()
            Error e
        | Ok newVersion ->
            match makeEvent updated with
            | Some evt ->
                OutboxRepository.insertEventTx tx (DomainEvent.eventType evt) (DomainEvent.payloadJson evt)
                |> ignore
            | None -> ()

            tx.Commit()
            Ok(updated, newVersion)

let private runTransition
    (connectionString: string)
    (id: string)
    (expectedVersion: int)
    (transition: InventoryLot -> Result<InventoryLot, DomainError>)
    : HttpHandler =
    fun next ctx -> task {
        let userId = SalesManagement.Api.Auth.getUserIdOrSystem ctx

        match executeTransition connectionString userId id expectedVersion transition with
        | Error err -> return! respondError err next ctx
        | Ok(updated, newVersion) ->
            invalidateLotCache ctx id
            return! json (toResponseBody updated newVersion) next ctx
    }

let private bodyParseError (ex: exn) : DomainError =
    ValidationFailed [ { Field = "body"; Message = ex.Message } ]

let private manufacturingCompletedEvent (date: DateOnly) (lot: InventoryLot) : DomainEvent option =
    match lot with
    | Manufactured _ ->
        let common = InventoryLot.common lot
        Some(LotManufacturingCompleted(LotNumber.toString common.LotNumber, date))
    | _ -> None

let private respondTransition (id: string) (result: Result<InventoryLot * int, DomainError>) : HttpHandler =
    fun next ctx -> task {
        match result with
        | Error err -> return! respondError err next ctx
        | Ok(updated, newVersion) ->
            invalidateLotCache ctx id
            return! json (toResponseBody updated newVersion) next ctx
    }

let completeManufacturingHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<DateVersionRequest>()

            match requireVersion dto.version, requireDate dto.date with
            | Error e, _ -> return! respondError e next ctx
            | _, Error e -> return! respondError e next ctx
            | Ok v, Ok date ->
                let userId = SalesManagement.Api.Auth.getUserIdOrSystem ctx

                let result =
                    executeTransitionWithEvent
                        connectionString
                        userId
                        id
                        v
                        (completeManufacturingTransition date)
                        (manufacturingCompletedEvent date)

                return! respondTransition id result next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let instructShippingHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<DeadlineVersionRequest>()

            match requireVersion dto.version, requireDeadline dto.deadline with
            | Error e, _ -> return! respondError e next ctx
            | _, Error e -> return! respondError e next ctx
            | Ok v, Ok deadline ->
                return! runTransition connectionString id v (instructShippingTransition deadline) next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let completeShippingHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<DateVersionRequest>()

            match requireVersion dto.version, requireDate dto.date with
            | Error e, _ -> return! respondError e next ctx
            | _, Error e -> return! respondError e next ctx
            | Ok v, Ok date -> return! runTransition connectionString id v (completeShippingTransition date) next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let cancelManufacturingCompletionHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<VersionOnlyRequest>()

            match requireVersion dto.version with
            | Error e -> return! respondError e next ctx
            | Ok v -> return! runTransition connectionString id v cancelManufacturingTransition next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let instructItemConversionHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<InstructItemConversionRequest>()

            match requireVersion dto.version, requireDestinationItem dto.destinationItem with
            | Error e, _ -> return! respondError e next ctx
            | _, Error e -> return! respondError e next ctx
            | Ok v, Ok destinationItem ->
                return! runTransition connectionString id v (instructItemConversionTransition destinationItem) next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let cancelItemConversionInstructionHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        try
            let! dto = ctx.BindJsonAsync<VersionOnlyRequest>()

            match requireVersion dto.version with
            | Error e -> return! respondError e next ctx
            | Ok v -> return! runTransition connectionString id v cancelItemConversionInstructionTransition next ctx
        with ex ->
            return! respondError (bodyParseError ex) next ctx
    }

let getLotHandler (connectionString: string) (id: string) : HttpHandler =
    fun next ctx -> task {
        match parseLotId id with
        | Error err -> return! respondError err next ctx
        | Ok lotNumber ->
            let cache = ctx.RequestServices.GetRequiredService<IMemoryCache>()
            let key = cacheKey lotNumber

            match cache.Get(key) with
            | :? LotResponse as cached ->
                Log.Debug("Cache HIT key={Key}", key)
                ctx.Response.Headers.["X-Cache"] <- StringValues "HIT"
                return! json cached next ctx
            | _ ->
                Log.Debug("Cache MISS key={Key}", key)
                ctx.Response.Headers.["X-Cache"] <- StringValues "MISS"
                use conn = new NpgsqlConnection(connectionString)
                conn.Open()

                match LotRepository.loadWithVersion conn lotNumber with
                | Error e -> return! respondError (InternalError e) next ctx
                | Ok None -> return! respondError (NotFound("Lot", id)) next ctx
                | Ok(Some(lot, version)) ->
                    let body = toResponseBody lot version

                    let entryOpts =
                        MemoryCacheEntryOptions(AbsoluteExpirationRelativeToNow = Nullable(TimeSpan.FromMinutes 5.0))

                    cache.Set(key, body, entryOpts) |> ignore
                    return! json body next ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose
        [ POST >=> route "/lots" >=> createLotHandler connectionString
          POST
          >=> routef "/lots/%s/complete-manufacturing" (completeManufacturingHandler connectionString)
          POST
          >=> routef "/lots/%s/instruct-shipping" (instructShippingHandler connectionString)
          POST
          >=> routef "/lots/%s/complete-shipping" (completeShippingHandler connectionString)
          POST
          >=> routef "/lots/%s/cancel-manufacturing-completion" (cancelManufacturingCompletionHandler connectionString)
          POST
          >=> routef "/lots/%s/instruct-item-conversion" (instructItemConversionHandler connectionString)
          DELETE
          >=> routef "/lots/%s/instruct-item-conversion" (cancelItemConversionInstructionHandler connectionString)
          GET >=> route "/lots/export" >=> LotCsvExport.exportLotsHandler connectionString
          GET >=> routef "/lots/%s" (getLotHandler connectionString) ]
