module SalesManagement.Api.LotListRoutes

open System
open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Domain.Errors
open SalesManagement.Infrastructure
open SalesManagement.Api.ProblemDetails

type LotSummary =
    { status: string
      lotNumber: string
      manufacturingCompletedDate: string option
      shippingDeadlineDate: string option
      shippedDate: string option
      destinationItem: string option
      version: int }

type ListLotsResponse =
    { items: LotSummary[]
      total: int
      limit: int
      offset: int }

let private formatDateOpt (d: DateOnly option) : string option =
    d |> Option.map (fun v -> v.ToString("yyyy-MM-dd"))

let private toLotSummary (item: LotListRepository.LotListItem) : LotSummary =
    { status = item.Status
      lotNumber = LotNumber.toString item.LotNumber
      manufacturingCompletedDate = formatDateOpt item.ManufacturingCompletedDate
      shippingDeadlineDate = formatDateOpt item.ShippingDeadlineDate
      shippedDate = formatDateOpt item.ShippedDate
      destinationItem = item.DestinationItem
      version = item.Version }

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

type private ListQuery =
    { Status: string option
      Limit: int
      Offset: int }

let private parseListQuery (ctx: HttpContext) : Result<ListQuery, DomainError> =
    let limitR = tryGetIntQuery ctx "limit"
    let offsetR = tryGetIntQuery ctx "offset"

    match limitR, offsetR with
    | Error msg, _ -> Error(ValidationFailed [ { Field = "limit"; Message = msg } ])
    | _, Error msg -> Error(ValidationFailed [ { Field = "offset"; Message = msg } ])
    | Ok limitOpt, Ok offsetOpt ->
        let limit = limitOpt |> Option.defaultValue 50
        let offset = offsetOpt |> Option.defaultValue 0

        if limit < 1 || limit > 200 then
            Error(
                ValidationFailed
                    [ { Field = "limit"
                        Message = "limit must be between 1 and 200" } ]
            )
        elif offset < 0 then
            Error(
                ValidationFailed
                    [ { Field = "offset"
                        Message = "offset must be >= 0" } ]
            )
        else
            Ok
                { Status = tryGetStringQuery ctx "status"
                  Limit = limit
                  Offset = offset }

let listLotsHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        match parseListQuery ctx with
        | Error err -> return! toResponse "Lot" err next ctx
        | Ok q ->
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()

            let result =
                LotListRepository.list
                    conn
                    { Status = q.Status
                      Limit = q.Limit
                      Offset = q.Offset }

            let response: ListLotsResponse =
                { items = result.Items |> List.map toLotSummary |> List.toArray
                  total = result.Total
                  limit = result.Limit
                  offset = result.Offset }

            return! json response next ctx
    }

let routes (connectionString: string) : HttpHandler =
    choose [ GET >=> route "/lots" >=> listLotsHandler connectionString ]
