module SalesManagement.Tests.IntegrationTests.ListEndpointsTests

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

type ListEndpointsFixture() =
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

[<CollectionDefinition("ListEndpoints")>]
type ListEndpointsCollection() =
    interface ICollectionFixture<ListEndpointsFixture>

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

let private parseJson (body: string) : JsonElement =
    use doc = JsonDocument.Parse body
    doc.RootElement.Clone()

let private createLotBody (year: int) (location: string) (seq: int) =
    sprintf
        """{
            "lotNumber": { "year": %d, "location": "%s", "seq": %d },
            "divisionCode": 1,
            "departmentCode": 10,
            "sectionCode": 100,
            "processCategory": 1,
            "inspectionCategory": 1,
            "manufacturingCategory": 1,
            "details": [{
                "itemCategory": "premium",
                "premiumCategory": "A",
                "productCategoryCode": "v1",
                "lengthSpecLower": 1.0,
                "thicknessSpecLower": 1.0,
                "thicknessSpecUpper": 2.0,
                "qualityGrade": "A",
                "count": 1,
                "quantity": 10.0,
                "inspectionResultCategory": "pass"
            }]
        }"""
        year
        location
        seq

let private createSalesCaseBody (lots: string list) =
    let lotsJson = lots |> List.map (sprintf "\"%s\"") |> String.concat ","

    sprintf
        """{
            "lots": [%s],
            "divisionCode": 1,
            "salesDate": "2026-04-15",
            "caseType": "direct"
        }"""
        lotsJson

[<Collection("ListEndpoints")>]
type ListLotsTests(fixture: ListEndpointsFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots returns items, total, limit, offset``() = task {
        use client = newClient fixture.Port

        // Seed two lots
        let! r1 = postJson client "/lots" (createLotBody 2026 "F12A" 1)
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode)
        let! r2 = postJson client "/lots" (createLotBody 2026 "F12A" 2)
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode)

        let! resp = getReq client "/lots?limit=20&offset=0"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.True(root.TryGetProperty("items") |> fst)
        Assert.True(root.TryGetProperty("total") |> fst)
        Assert.True(root.TryGetProperty("limit") |> fst)
        Assert.True(root.TryGetProperty("offset") |> fst)
        Assert.Equal(20, root.GetProperty("limit").GetInt32())
        Assert.Equal(0, root.GetProperty("offset").GetInt32())
        Assert.True(root.GetProperty("total").GetInt32() >= 2)
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots?status=manufacturing returns 200``() = task {
        use client = newClient fixture.Port

        let! _ = postJson client "/lots" (createLotBody 2026 "F12B" 1)
        let! resp = getReq client "/lots?status=manufacturing"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.True(root.TryGetProperty("items") |> fst)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots with limit > 200 returns 400``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/lots?limit=500"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots with negative offset returns 400``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/lots?offset=-1"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

[<Collection("ListEndpoints")>]
type ListSalesCasesTests(fixture: ListEndpointsFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases returns items, total``() = task {
        use client = newClient fixture.Port

        let! resp = getReq client "/sales-cases?limit=10&offset=0"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.True(root.TryGetProperty("items") |> fst)
        Assert.True(root.TryGetProperty("total") |> fst)
        Assert.Equal(10, root.GetProperty("limit").GetInt32())
        Assert.Equal(0, root.GetProperty("offset").GetInt32())
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases?status=appraised returns 200``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/sales-cases?status=appraised"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases with limit > 200 returns 400``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/sales-cases?limit=500"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

[<Collection("ListEndpoints")>]
type ListEndpointsOpenApiSpecTests(fixture: ListEndpointsFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml contains listLots and listSalesCases``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("operationId: listLots", body)
        Assert.Contains("operationId: listSalesCases", body)
    }
