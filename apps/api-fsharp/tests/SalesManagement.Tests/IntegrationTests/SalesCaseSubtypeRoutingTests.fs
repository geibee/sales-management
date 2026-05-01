module SalesManagement.Tests.IntegrationTests.SalesCaseSubtypeRoutingTests

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

type SalesCaseSubtypeRoutingFixture() =
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

[<CollectionDefinition("SalesCaseSubtypeRouting")>]
type SalesCaseSubtypeRoutingCollection() =
    interface ICollectionFixture<SalesCaseSubtypeRoutingFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let private deleteJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Delete, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

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
    return extractStr doc.RootElement "salesCaseNumber"
}

[<Collection("SalesCaseSubtypeRouting")>]
type ReservationUrlTests(fixture: SalesCaseSubtypeRoutingFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation mutations work under /sales-cases/{id}/reservation/...``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 8500 + r.Next(0, 100)
        let location = sprintf "S18E%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "reservation" [| lotId |]

        // reservation appraisal
        let! appraisalResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/appraisals" caseId)
                """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        Assert.Equal(HttpStatusCode.OK, appraisalResp.StatusCode)

        // reservation determine
        let! determineResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":2}"""

        Assert.Equal(HttpStatusCode.OK, determineResp.StatusCode)

        // reservation cancel determination (DELETE /determination)
        let! cancelResp =
            deleteJson client (sprintf "/sales-cases/%s/reservation/determination" caseId) """{"version":3}"""

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode)

        // re-determine and then deliver
        let! redoResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":4}"""

        Assert.Equal(HttpStatusCode.OK, redoResp.StatusCode)

        let! deliverResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/delivery" caseId)
                """{"deliveryDate":"2026-01-30","version":5}"""

        Assert.Equal(HttpStatusCode.OK, deliverResp.StatusCode)

        // Persistence regression guard: GET must echo the deliveryDate the client posted.
        let! detailResp = client.GetAsync(sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode)
        use detailDoc = parseBody detailResp
        let delivery = detailDoc.RootElement.GetProperty "delivery"
        Assert.Equal(JsonValueKind.Object, delivery.ValueKind)
        Assert.Equal("2026-01-30", delivery.GetProperty("deliveredDate").GetString())
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``old /reservation-cases/{id}/... URLs return 404``() = task {
        use client = newClient fixture.Port

        let probe (method: HttpMethod) (path: string) : Task<HttpStatusCode> = task {
            let req = new HttpRequestMessage(method, path)
            req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            let! resp = client.SendAsync req
            return resp.StatusCode
        }

        let! s1 = probe HttpMethod.Post "/reservation-cases/2026-01-001/appraisals"
        Assert.Equal(HttpStatusCode.NotFound, s1)
        let! s2 = probe HttpMethod.Post "/reservation-cases/2026-01-001/determine"
        Assert.Equal(HttpStatusCode.NotFound, s2)
        let! s3 = probe HttpMethod.Delete "/reservation-cases/2026-01-001/determination"
        Assert.Equal(HttpStatusCode.NotFound, s3)
        let! s4 = probe HttpMethod.Post "/reservation-cases/2026-01-001/delivery"
        Assert.Equal(HttpStatusCode.NotFound, s4)
    }

[<Collection("SalesCaseSubtypeRouting")>]
type ConsignmentUrlTests(fixture: SalesCaseSubtypeRoutingFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment mutations work under /sales-cases/{id}/consignment/...``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 8700 + r.Next(0, 100)
        let location = sprintf "S18C%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "consignment" [| lotId |]

        // consignment designate
        let! designateResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        Assert.Equal(HttpStatusCode.OK, designateResp.StatusCode)

        // consignment cancel designation (DELETE /designation)
        let! cancelResp =
            deleteJson client (sprintf "/sales-cases/%s/consignment/designation" caseId) """{"version":2}"""

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode)

        // re-designate and then record result
        let! redoResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":3}"""

        Assert.Equal(HttpStatusCode.OK, redoResp.StatusCode)

        let! resultResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/result" caseId)
                """{"resultDate":"2026-01-30","resultAmount":480000,"version":4}"""

        Assert.Equal(HttpStatusCode.OK, resultResp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``old /consignment-cases/{id}/... URLs return 404``() = task {
        use client = newClient fixture.Port

        let probe (method: HttpMethod) (path: string) : Task<HttpStatusCode> = task {
            let req = new HttpRequestMessage(method, path)
            req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            let! resp = client.SendAsync req
            return resp.StatusCode
        }

        let! s1 = probe HttpMethod.Post "/consignment-cases/2026-01-001/designate"
        Assert.Equal(HttpStatusCode.NotFound, s1)
        let! s2 = probe HttpMethod.Delete "/consignment-cases/2026-01-001/designation"
        Assert.Equal(HttpStatusCode.NotFound, s2)
        let! s3 = probe HttpMethod.Post "/consignment-cases/2026-01-001/result"
        Assert.Equal(HttpStatusCode.NotFound, s3)
    }

[<Collection("SalesCaseSubtypeRouting")>]
type SubtypeRoutingOpenApiSpecTests(fixture: SalesCaseSubtypeRoutingFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml exposes new sales-cases reservation/consignment paths and not the old ones``() = task {
        use client = newClient fixture.Port
        let! resp = client.GetAsync "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()

        Assert.Contains("/sales-cases/{id}/reservation/appraisals", body)
        Assert.Contains("/sales-cases/{id}/reservation/determine", body)
        Assert.Contains("/sales-cases/{id}/reservation/determination", body)
        Assert.Contains("/sales-cases/{id}/reservation/delivery", body)
        Assert.Contains("/sales-cases/{id}/consignment/designate", body)
        Assert.Contains("/sales-cases/{id}/consignment/designation", body)
        Assert.Contains("/sales-cases/{id}/consignment/result", body)

        Assert.DoesNotContain("\n  /reservation-cases/", body)
        Assert.DoesNotContain("\n  /consignment-cases/", body)
    }
