module SalesManagement.Tests.IntegrationTests.SalesCaseRetrievalTests

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

type SalesCaseRetrievalFixture() =
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

[<CollectionDefinition("SalesCaseRetrieval")>]
type SalesCaseRetrievalCollection() =
    interface ICollectionFixture<SalesCaseRetrievalFixture>

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

let private extractStr (root: JsonElement) (name: string) : string =
    let p = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty(name, p), sprintf "field '%s' missing in: %s" name (root.GetRawText()))
    (!p).GetString()

let private hasNonNull (root: JsonElement) (name: string) : bool =
    let p = ref Unchecked.defaultof<JsonElement>

    if root.TryGetProperty(name, p) then
        (!p).ValueKind <> JsonValueKind.Null
    else
        false

let private parseBody (resp: HttpResponseMessage) : JsonDocument =
    let body = resp.Content.ReadAsStringAsync().Result
    JsonDocument.Parse body

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

[<Collection("SalesCaseRetrieval")>]
type SalesCaseRetrievalDetailTests(fixture: SalesCaseRetrievalFixture) =

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases on missing id returns 404 problem+json``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/sales-cases/9999-99-999"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases with malformed id returns 400 problem+json``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/sales-cases/invalid-id"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``direct sales case can be retrieved with caseType=direct``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 5000 + r.Next(0, 500)
        let location = sprintf "D%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "direct" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        use doc = parseBody resp
        let root = doc.RootElement
        Assert.Equal(caseId, extractStr root "salesCaseNumber")
        Assert.Equal("direct", extractStr root "caseType")
        Assert.Equal("before_appraisal", extractStr root "status")
        let lotsP = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("lots", lotsP))
        Assert.Equal(JsonValueKind.Array, (!lotsP).ValueKind)
        Assert.Equal(1, (!lotsP).GetArrayLength())
        Assert.Equal(lotId, (!lotsP).[0].GetString())
        let dc = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("divisionCode", dc))
        Assert.Equal(1, (!dc).GetInt32())
        Assert.Equal("2026-01-15", extractStr root "salesDate")
        let v = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("version", v))
        Assert.True((!v).GetInt32() >= 1)
        // direct subtype-specific fields are present (may be null pre-state-transition)
        Assert.True(root.TryGetProperty("appraisal", ref Unchecked.defaultof<JsonElement>))
        Assert.True(root.TryGetProperty("contract", ref Unchecked.defaultof<JsonElement>))
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation sales case can be retrieved with caseType=reservation and status transitions reflect``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 5500 + r.Next(0, 500)
        let location = sprintf "E%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "reservation" [| lotId |]

        // initial GET
        let! resp1 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)
        use doc1 = parseBody resp1
        Assert.Equal("reservation", extractStr doc1.RootElement "caseType")
        Assert.Equal("before_reservation", extractStr doc1.RootElement "status")
        Assert.False(hasNonNull doc1.RootElement "reservationPrice")

        // create reservation appraisal
        let appraisalBody =
            """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        let! aResp = postJson client (sprintf "/sales-cases/%s/reservation/appraisals" caseId) appraisalBody
        Assert.Equal(HttpStatusCode.OK, aResp.StatusCode)

        // GET reflects status + reservationPrice field present
        let! resp2 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode)
        use doc2 = parseBody resp2
        Assert.Equal("reserved", extractStr doc2.RootElement "status")
        Assert.True(hasNonNull doc2.RootElement "reservationPrice")
        let ea = doc2.RootElement.GetProperty("reservationPrice")
        Assert.Equal("2026-01-20", extractStr ea "appraisalDate")
        Assert.Equal(500000, ea.GetProperty("reservedAmount").GetInt32())

        // determine (case version was bumped to 2 by appraisal insert)
        let detBody =
            """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":2}"""

        let! dResp = postJson client (sprintf "/sales-cases/%s/reservation/determine" caseId) detBody
        Assert.Equal(HttpStatusCode.OK, dResp.StatusCode)

        let! resp3 = getReq client (sprintf "/sales-cases/%s" caseId)
        use doc3 = parseBody resp3
        Assert.Equal("reservation_confirmed", extractStr doc3.RootElement "status")
        Assert.True(hasNonNull doc3.RootElement "determination")
        let det = doc3.RootElement.GetProperty("determination")
        Assert.Equal(480000, det.GetProperty("determinedAmount").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment sales case can be retrieved with caseType=consignment``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 6000 + r.Next(0, 500)
        let location = sprintf "C%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "consignment" [| lotId |]

        let! resp1 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)
        use doc1 = parseBody resp1
        Assert.Equal("consignment", extractStr doc1.RootElement "caseType")
        Assert.Equal("before_consignment", extractStr doc1.RootElement "status")
        Assert.False(hasNonNull doc1.RootElement "consignor")

        // designate consignor
        let designate =
            """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        let! dResp = postJson client (sprintf "/sales-cases/%s/consignment/designate" caseId) designate
        Assert.Equal(HttpStatusCode.OK, dResp.StatusCode)

        let! resp2 = getReq client (sprintf "/sales-cases/%s" caseId)
        use doc2 = parseBody resp2
        Assert.Equal("consignment_designated", extractStr doc2.RootElement "status")
        Assert.True(hasNonNull doc2.RootElement "consignor")
        let co = doc2.RootElement.GetProperty("consignor")
        Assert.Equal("Acme", extractStr co "consignorName")
        Assert.Equal("C001", extractStr co "consignorCode")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases does not exist for /reservation-cases or /consignment-cases``() = task {
        use client = newClient fixture.Port
        // GET to subtype-specific URLs should NOT return 200 (no GET handler registered)
        let! resp1 = getReq client "/reservation-cases/2026-01-001"
        Assert.NotEqual(HttpStatusCode.OK, resp1.StatusCode)
        let! resp2 = getReq client "/consignment-cases/2026-01-001"
        Assert.NotEqual(HttpStatusCode.OK, resp2.StatusCode)
    }
