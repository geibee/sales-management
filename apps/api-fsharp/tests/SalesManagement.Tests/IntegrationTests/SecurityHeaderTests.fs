module SalesManagement.Tests.IntegrationTests.SecurityHeaderTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading.Tasks
open System.Web
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

type SecurityHeaderFixture() =
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
                       // Point upstream at a closed port so calls fail fast (502/503).
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

[<CollectionDefinition("SecurityHeader")>]
type SecurityHeaderCollection() =
    interface ICollectionFixture<SecurityHeaderFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private getReq (client: HttpClient) (path: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)
    client.SendAsync req

let private headerValue (resp: HttpResponseMessage) (name: string) : string option =
    let succ, vals = resp.Headers.TryGetValues name

    if succ then
        Some(String.Join(",", vals))
    else
        let succ2, vals2 = resp.Content.Headers.TryGetValues name

        if succ2 then Some(String.Join(",", vals2)) else None

[<Collection("SecurityHeader")>]
type SecurityHeadersTests(fixture: SecurityHeaderFixture) =

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /health includes X-Content-Type-Options nosniff``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let v = headerValue resp "X-Content-Type-Options"
        Assert.Equal(Some "nosniff", v)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /health includes Cross-Origin-Resource-Policy same-origin``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let v = headerValue resp "Cross-Origin-Resource-Policy"
        Assert.Equal(Some "same-origin", v)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /openapi.yaml includes security headers``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        Assert.Equal(Some "nosniff", headerValue resp "X-Content-Type-Options")
        Assert.Equal(Some "same-origin", headerValue resp "Cross-Origin-Resource-Policy")
    }

[<Collection("SecurityHeader")>]
type LotIdValidationTests(fixture: SecurityHeaderFixture) =

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with lotId=lotId returns 400``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/api/external/price-check?lotId=lotId"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with empty lotId returns 400``() = task {
        use client = newClient fixture.Port
        let! resp = getReq client "/api/external/price-check?lotId="
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with newline-containing lotId returns 400``() = task {
        use client = newClient fixture.Port
        let encoded = HttpUtility.UrlEncode("foo\nbar")
        let! resp = getReq client (sprintf "/api/external/price-check?lotId=%s" encoded)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with valid lotId format proceeds to upstream``() = task {
        use client = newClient fixture.Port
        // Upstream is intentionally unreachable; expect 502/503 (not 400).
        let! resp = getReq client "/api/external/price-check?lotId=2026-A-001"
        Assert.NotEqual(HttpStatusCode.BadRequest, resp.StatusCode)
        let code = int resp.StatusCode
        Assert.True(code = 502 || code = 503, sprintf "expected 502 or 503, got %d" code)
    }
