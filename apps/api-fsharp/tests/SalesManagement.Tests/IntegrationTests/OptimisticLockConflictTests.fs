module SalesManagement.Tests.IntegrationTests.OptimisticLockConflictTests

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

type OptimisticLockConflictFixture() =
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

[<CollectionDefinition("OptimisticLockConflict")>]
type OptimisticLockConflictCollection() =
    interface ICollectionFixture<OptimisticLockConflictFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let private putJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Put, path)
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

let private appraisalBody (lotId: string) (version: int) =
    sprintf
        """{"type":"normal","appraisalDate":"2026-01-20","deliveryDate":"2026-01-25",
            "salesMarket":"market","baseUnitPriceDate":"2026-01-01",
            "periodAdjustmentRateDate":"2026-01-01","counterpartyAdjustmentRateDate":"2026-01-01",
            "taxExcludedEstimatedTotal":100000,
            "lotAppraisals":[{"lotNumber":"%s","detailAppraisals":[{"detailIndex":1,"baseUnitPrice":1000,"periodAdjustmentRate":1.0,"counterpartyAdjustmentRate":1.0}]}],
            "version":%d}"""
        lotId
        version

[<Collection("OptimisticLockConflict")>]
type SalesCaseConflictTests(fixture: OptimisticLockConflictFixture) =

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``PUT sales-cases appraisals with stale version returns 409``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 9000 + r.Next(0, 500)
        let location = sprintf "VA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "direct" [| lotId |]

        // First appraisal with version=1 → should succeed (case version becomes 2)
        let! firstResp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) (appraisalBody lotId 1)
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)
        use firstDoc = parseBody firstResp
        Assert.Equal(2, firstDoc.RootElement.GetProperty("version").GetInt32())

        // PUT update with stale version=1 → should be 409
        let! conflictResp = putJson client (sprintf "/sales-cases/%s/appraisals" caseId) (appraisalBody lotId 1)
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

[<Collection("OptimisticLockConflict")>]
type ReservationConflictTests(fixture: OptimisticLockConflictFixture) =

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``POST sales-cases reservation appraisals with stale version returns 409``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 9500 + r.Next(0, 500)
        let location = sprintf "VE%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "reservation" [| lotId |]

        // First insert with version=1 → success
        let! firstResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/appraisals" caseId)
                """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)

        // Determination request with stale version=1 (case is now at version=2) → 409
        let! conflictResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":1}"""

        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

[<Collection("OptimisticLockConflict")>]
type ConsignmentConflictTests(fixture: OptimisticLockConflictFixture) =

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``POST sales-cases consignment designate twice with stale version returns 409``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 9700 + r.Next(0, 200)
        let location = sprintf "VC%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "consignment" [| lotId |]

        // First designate with version=1 → success
        let! firstResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)

        // Result with stale version=1 (case is now at version=2) → 409
        let! conflictResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/result" caseId)
                """{"resultDate":"2026-01-30","resultAmount":480000,"version":1}"""

        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }
