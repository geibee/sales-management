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

let private parseStatusFilter (ctx: HttpContext) : string option =
    match ctx.Request.Query.TryGetValue "status" with
    | true, v ->
        let s = v.ToString()
        if String.IsNullOrEmpty s then None else Some s
    | false, _ -> None

let exportLotsHandler (connectionString: string) : HttpHandler =
    fun (_: HttpFunc) (ctx: HttpContext) -> task {
        let statusFilter = parseStatusFilter ctx

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
        return Some ctx
    }
