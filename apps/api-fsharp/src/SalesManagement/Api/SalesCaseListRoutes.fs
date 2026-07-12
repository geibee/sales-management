module SalesManagement.Api.SalesCaseListRoutes

open System
open Microsoft.AspNetCore.Http
open Giraffe
open Npgsql
open SalesManagement.Infrastructure
open SalesManagement.Api.SalesCaseDtos
open SalesManagement.Api.ProblemDetails

// 販売案件一覧 (GET /sales-cases)。SalesCaseRoutes のモジュール肥大化
// (FSharpLint FL0029) を避けるため一覧系のみ分離した。

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

let private listSalesCases
    (connectionString: string)
    (status: string option)
    (caseType: string option)
    (limit: int)
    (offset: int)
    : SalesCaseListRepository.SalesCaseListResult =
    match status with
    | Some value when value.Contains '\u0000' ->
        // status は契約上 free-form。NUL は PostgreSQL の text に渡せないが、
        // 保存済み status と一致し得ないため安全に空結果として扱う。
        { Items = []
          Total = 0
          Limit = limit
          Offset = offset }
    | _ ->
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()

        SalesCaseListRepository.list
            conn
            { Status = status
              CaseType = caseType
              Limit = limit
              Offset = offset }

let listSalesCasesHandler (connectionString: string) : HttpHandler =
    fun next ctx -> task {
        match QueryGuard.tryFindInvalidQuery [ "status"; "caseType"; "limit"; "offset" ] ctx with
        | Some err -> return! toResponse "SalesCase" err next ctx
        | None ->

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
                elif
                    (match tryGetStringQuery ctx "caseType" with
                     | Some c -> not (List.contains c [ "direct"; "reservation"; "consignment" ])
                     | None -> false)
                then
                    // openapi.yaml の enum と対。未知の値を無視して全件返す fail-open を防ぐ
                    return! badRequest "caseType must be one of: direct, reservation, consignment" next ctx
                else
                    let status = tryGetStringQuery ctx "status"
                    let caseType = tryGetStringQuery ctx "caseType"

                    let result = listSalesCases connectionString status caseType limit offset

                    let response: ListSalesCasesResponse =
                        { items = result.Items |> List.map toSalesCaseSummary |> List.toArray
                          total = result.Total
                          limit = result.Limit
                          offset = result.Offset }

                    return! json response next ctx
    }
