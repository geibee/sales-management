module SalesManagement.Tests.ContractValidationTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open Microsoft.OpenApi.Readers
open Xunit
open SalesManagement.Tests.Support.OpenApiValidation

let cases: obj[] seq =
    use source =
        JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "response-contracts.json"))
        )

    [ for item in source.RootElement.EnumerateArray() do
          yield [| box (item.GetProperty("id").GetString()); box (item.GetRawText()) |] ]

[<Theory>]
[<MemberData(nameof cases)>]
let ``共通応答fixtureの契約適合を検査する`` (_id: string) (json: string) = task {
    use fixture = JsonDocument.Parse json
    let item = fixture.RootElement

    let value name fallback =
        match item.TryGetProperty(name: string) with
        | true, found -> found.GetString()
        | _ -> fallback

    let schema = item.GetProperty("schema").GetRawText()
    let media = value "mediaType" "application/json"
    let responseStatus = value "responseStatus" "200"

    let spec =
        sprintf
            """{"openapi":"3.0.3","info":{"title":"fixture","version":"1"},"paths":{"/fixture":{"get":{"operationId":"fixture","responses":{"%s":{"description":"fixture","content":{"%s":{"schema":%s}}}}}}}}"""
            responseStatus
            media
            schema

    let mutable diagnostic = Unchecked.defaultof<OpenApiDiagnostic>
    let document = OpenApiStringReader().Read(spec, &diagnostic)
    Assert.Empty diagnostic.Errors
    use request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/fixture")

    let status =
        match item.TryGetProperty "status" with
        | true, found -> found.GetInt32()
        | _ -> 200

    use response = new HttpResponseMessage(enum<HttpStatusCode> status)
    response.Content <- new StringContent(item.GetProperty("body").GetString(), Encoding.UTF8)
    response.Content.Headers.Remove "Content-Type" |> ignore
    let contentType = value "contentType" "application/json"

    if contentType <> "" then
        response.Content.Headers.TryAddWithoutValidation("Content-Type", contentType)
        |> ignore

    let! error = Record.ExceptionAsync(fun () -> validateWithDocument document request response CancellationToken.None)

    if item.GetProperty("valid").GetBoolean() then
        Assert.Null error
    else
        Assert.NotNull error
}
