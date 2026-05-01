module SalesManagement.Tests.IntegrationTests.SalesCaseDetailTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

type SalesCaseDetailFixture() =
    let mutable container: PostgreSqlContainer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable connStr: string = ""

    member _.Port = port
    member _.ConnectionString = connStr

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                container <-
                    PostgreSqlBuilder()
                        .WithImage("postgres:16-alpine")
                        .WithDatabase("sales_management")
                        .WithUsername("app")
                        .WithPassword("app")
                        .Build()

                do! container.StartAsync()
                connStr <- container.GetConnectionString()

                let upgrader =
                    DeployChanges.To
                        .PostgresqlDatabase(connStr)
                        .WithScriptsFromFileSystem(migrationsDir)
                        .LogToConsole()
                        .Build()

                let result = upgrader.PerformUpgrade()

                if not result.Successful then
                    failwithf "Migration failed: %s" (result.Error.ToString())

                port <- getFreePort ()

                let args =
                    [| sprintf "--Server:Port=%d" port
                       sprintf "--Database:ConnectionString=%s" connStr
                       "--Authentication:Enabled=false"
                       "--RateLimit:PermitLimit=100000"
                       "--RateLimit:WindowSeconds=60"
                       "--Outbox:PollIntervalMs=500"
                       "--ExternalApi:PricingUrl=http://127.0.0.1:1"
                       "--ExternalApi:TimeoutMs=500"
                       "--ExternalApi:RetryCount=0"
                       "--Logging:LogLevel:Default=Warning" |]

                app <- createApp args
                do! app.StartAsync()
            }
            :> Task

        member _.DisposeAsync() : Task =
            task {
                if not (isNull (box app)) then
                    try
                        do! app.StopAsync()
                    with _ ->
                        ()

                if not (isNull (box container)) then
                    do! container.DisposeAsync()
            }
            :> Task

[<CollectionDefinition("SalesCaseDetail")>]
type SalesCaseDetailCollection() =
    interface ICollectionFixture<SalesCaseDetailFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let private getReq (client: HttpClient) (path: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)
    client.SendAsync req

let private readBody (resp: HttpResponseMessage) : Task<string> = resp.Content.ReadAsStringAsync()

let private parseBody (resp: HttpResponseMessage) : JsonDocument =
    let body = resp.Content.ReadAsStringAsync().Result
    JsonDocument.Parse body

let private extractStr (root: JsonElement) (name: string) : string =
    let p = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty(name, p), sprintf "field '%s' missing in: %s" name (root.GetRawText()))
    (!p).GetString()

let private createLotBody (year: int) (location: string) (seq: int) =
    sprintf
        """{
            "lotNumber": {"year": %d, "location": "%s", "seq": %d},
            "divisionCode": 1, "departmentCode": 1, "sectionCode": 1,
            "processCategory": 1, "inspectionCategory": 1, "manufacturingCategory": 1,
            "details": [
                {"itemCategory": "general", "premiumCategory": "", "productCategoryCode": "v",
                 "lengthSpecLower": 1.0, "thicknessSpecLower": 1.0, "thicknessSpecUpper": 2.0,
                 "qualityGrade": "A", "count": 1, "quantity": 1.0, "inspectionResultCategory": ""}
            ]
        }"""
        year
        location
        seq

let private completeManufacturingBody (version: int) (date: string) =
    sprintf """{"date":"%s","version":%d}""" date version

let private setupManufacturedLot (client: HttpClient) (year: int) (location: string) (seq: int) : Task<string> = task {
    let! createResp = postJson client "/lots" (createLotBody year location seq)
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)
    let lotId = sprintf "%d-%s-%03d" year location seq

    let! mfgResp =
        postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

    Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
    return lotId
}

let private createSalesCase (client: HttpClient) (caseType: string) (lotIds: string[]) : Task<string> = task {
    let lotsJson = String.Join(",", lotIds |> Array.map (sprintf "\"%s\""))

    let body =
        sprintf """{"lots":[%s],"divisionCode":1,"salesDate":"2026-01-15","caseType":"%s"}""" lotsJson caseType

    let! resp = postJson client "/sales-cases" body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    use doc = parseBody resp
    let p = ref Unchecked.defaultof<JsonElement>
    Assert.True(doc.RootElement.TryGetProperty("salesCaseNumber", p))
    return (!p).GetString()
}

[<Collection("SalesCaseDetail")>]
type SalesCaseDetailOpenApiSpecTests(fixture: SalesCaseDetailFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml declares getSalesCase under /sales-cases/{id}``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("operationId: getSalesCase", body)
        Assert.Contains("SalesCaseDetailResponse", body)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml SalesCaseDetailResponse lists required and subtype fields``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/openapi.yaml"
        let! body = readBody resp

        // 必須フィールド
        for f in
            [ "salesCaseNumber"
              "caseType"
              "status"
              "lots"
              "divisionCode"
              "salesDate"
              "version" ] do
            Assert.Contains(f, body)

        // direct 案件サブタイプフィールド
        for f in [ "appraisal"; "contract"; "shippingInstruction"; "shippingCompletion" ] do
            Assert.Contains(f, body)

        // reservation 案件サブタイプフィールド
        for f in [ "reservationPrice"; "determination"; "delivery" ] do
            Assert.Contains(f, body)

        // consignment 案件サブタイプフィールド
        for f in [ "consignor"; "result" ] do
            Assert.Contains(f, body)
    }

[<Collection("SalesCaseDetail")>]
type DetailRetrievalTests(fixture: SalesCaseDetailFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases/{id} returns 404 for unknown id``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/sales-cases/9999-99-999"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``direct sales case returns caseType=direct``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 7000 + r.Next(0, 500)
        let location = sprintf "DA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "direct" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        use doc = parseBody resp
        Assert.Equal("direct", extractStr doc.RootElement "caseType")
        Assert.Equal(caseId, extractStr doc.RootElement "salesCaseNumber")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation sales case returns caseType=reservation``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 7500 + r.Next(0, 500)
        let location = sprintf "EA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "reservation" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        use doc = parseBody resp
        Assert.Equal("reservation", extractStr doc.RootElement "caseType")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment sales case returns caseType=consignment``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 8000 + r.Next(0, 500)
        let location = sprintf "CA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "consignment" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        use doc = parseBody resp
        Assert.Equal("consignment", extractStr doc.RootElement "caseType")
    }
