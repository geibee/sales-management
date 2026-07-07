module SalesManagement.Api.LotCsvExport

open System
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Giraffe
open Npgsql
open SalesManagement.Domain.Types
open SalesManagement.Infrastructure

let private csvEscape (s: string) : string =
    let raw = if isNull s then "" else s
    let escaped = raw.Replace("\"", "\"\"")
    sprintf "\"%s\"" escaped

let private csvLine (fields: string list) : string =
    fields |> List.map csvEscape |> String.concat ","

/// openapi.yaml の LotStatus enum と対。未知の値は 400 で拒否する。
let private knownLotStatuses =
    Set.ofList
        [ "manufacturing"
          "manufactured"
          "shipping_instructed"
          "shipped"
          "conversion_instructed" ]

/// format クエリ (現状 csv のみ)。openapi.yaml の enum と対。
let private validateFormat (ctx: HttpContext) : Result<unit, string> =
    match ctx.Request.Query.TryGetValue "format" with
    | true, v when v.ToString() <> "csv" -> Error "format must be: csv"
    | _ -> Ok()

let private parseStatusFilter (ctx: HttpContext) : Result<string option, string> =
    match ctx.Request.Query.TryGetValue "status" with
    | true, v ->
        let s = v.ToString()

        if String.IsNullOrEmpty s then
            Ok None
        elif knownLotStatuses.Contains s then
            Ok(Some s)
        else
            Error("status must be one of: " + String.Join(", ", knownLotStatuses))
    | false, _ -> Ok None

let private writeCsv (connectionString: string) (statusFilter: string option) (ctx: HttpContext) = task {
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    let rows = LotRepository.listForCsv conn statusFilter

    // Codepage 932 = Windows-31J (Microsoft's Shift_JIS variant); the IANA
    // alias "windows-31j" is not a registered Encoding name on .NET, but
    // codepage 932 is once CodePagesEncodingProvider is registered.
    let encoding = Encoding.GetEncoding(932)
    ctx.Response.ContentType <- "text/csv; charset=windows-31j"

    let filename = sprintf "lots_%s.csv" (DateTime.Now.ToString("yyyyMMdd"))

    ctx.Response.Headers.["Content-Disposition"] <- StringValues(sprintf "attachment; filename=\"%s\"" filename)

    use writer = new StreamWriter(ctx.Response.Body, encoding, 1024, true)
    do! writer.WriteLineAsync(csvLine [ "ロット番号"; "事業部"; "状態"; "製造完了日" ])

    for row in rows do
        let lotStr = LotNumber.toString row.LotNumber
        let divisionStr = string row.DivisionCode

        let mfgStr =
            match row.ManufacturingCompletedDate with
            | Some d -> d.ToString("yyyy-MM-dd")
            | None -> ""

        do! writer.WriteLineAsync(csvLine [ lotStr; divisionStr; row.Status; mfgStr ])

    do! writer.FlushAsync()
}

let exportLotsHandler (connectionString: string) : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) -> task {
        match QueryGuard.tryFindInvalidQuery [ "format"; "status" ] ctx with
        | Some err -> return! ProblemDetails.toResponse "Lot" err next ctx
        | None ->

            match validateFormat ctx with
            | Error message ->
                let err =
                    SalesManagement.Domain.Errors.ValidationFailed [ { Field = "format"; Message = message } ]

                return! ProblemDetails.toResponse "Lot" err next ctx
            | Ok() ->

                match parseStatusFilter ctx with
                | Error message ->
                    let err =
                        SalesManagement.Domain.Errors.ValidationFailed [ { Field = "status"; Message = message } ]

                    return! ProblemDetails.toResponse "Lot" err next ctx
                | Ok statusFilter ->
                    do! writeCsv connectionString statusFilter ctx
                    return Some ctx
    }
