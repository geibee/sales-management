module SalesManagement.Tests.IntegrationTests.RateLimitAndCacheTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Npgsql
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

type FixtureConfig =
    { PermitLimit: int
      WindowSeconds: int
      ShutdownSeconds: int }

let private defaultConfig =
    { PermitLimit = 10000
      WindowSeconds = 60
      ShutdownSeconds = 30 }

type RateLimitAndCacheFixtureBase(config: FixtureConfig) =
    let mutable container: PostgreSqlContainer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable connStr: string = ""

    member _.Port = port
    member _.ConnectionString = connStr
    member _.App = app

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
                       sprintf "--RateLimit:PermitLimit=%d" config.PermitLimit
                       sprintf "--RateLimit:WindowSeconds=%d" config.WindowSeconds
                       sprintf "--Hosting:ShutdownTimeoutSeconds=%d" config.ShutdownSeconds
                       "--Outbox:PollIntervalMs=500"
                       "--Logging:LogLevel:Default=Warning"
                       // RateLimitAndCache exercises /test/slow as a cheap unauthenticated
                       // endpoint; that route is only registered in Development.
                       "--environment=Development" |]

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

type RateLimitAndCacheDefaultFixture() =
    inherit RateLimitAndCacheFixtureBase(defaultConfig)

type RateLimitAndCacheRateLimitFixture() =
    inherit
        RateLimitAndCacheFixtureBase(
            { PermitLimit = 5
              WindowSeconds = 60
              ShutdownSeconds = 30 }
        )

type RateLimitAndCacheShutdownFixture() =
    inherit RateLimitAndCacheFixtureBase(defaultConfig)

[<CollectionDefinition("RateLimitAndCacheDefault")>]
type RateLimitAndCacheDefaultCollection() =
    interface ICollectionFixture<RateLimitAndCacheDefaultFixture>

[<CollectionDefinition("RateLimitAndCacheRateLimit")>]
type RateLimitAndCacheRateLimitCollection() =
    interface ICollectionFixture<RateLimitAndCacheRateLimitFixture>

[<CollectionDefinition("RateLimitAndCacheShutdown")>]
type RateLimitAndCacheShutdownCollection() =
    interface ICollectionFixture<RateLimitAndCacheShutdownFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 30.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
    client.SendAsync req

let private getReq (client: HttpClient) (path: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)
    client.SendAsync req

let private uniqueLot () =
    let r = Random()
    let year = 6000 + r.Next(0, 3000)
    let location = "G"
    let seq = r.Next(1, 999)
    let id = sprintf "%d-%s-%03d" year location seq
    year, location, seq, id

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

[<Collection("RateLimitAndCacheRateLimit")>]
type RateLimitAndCacheRateLimitTests(fixture: RateLimitAndCacheRateLimitFixture) =

    [<Fact>]
    [<Trait("Category", "RateLimitAndCache")>]
    [<Trait("Category", "Integration")>]
    member _.``rate limiter returns 429 with Retry-After header once permits exhausted``() = task {
        use client = newClient fixture.Port

        // Use /test/slow which is unauthenticated and cheap, so we focus on rate-limit behavior.
        let mutable okCount = 0
        let mutable rejectedCount = 0
        let mutable retryAfter: string = ""

        for _ in 1..20 do
            let! resp = getReq client "/test/slow?ms=0"

            match int resp.StatusCode with
            | 200 -> okCount <- okCount + 1
            | 429 ->
                rejectedCount <- rejectedCount + 1

                if String.IsNullOrEmpty retryAfter then
                    let mutable values: seq<string> = Seq.empty

                    if resp.Headers.TryGetValues("Retry-After", &values) then
                        retryAfter <- values |> Seq.head
            | _ -> ()

        Assert.True(okCount > 0, sprintf "expected some 200s, got %d" okCount)
        Assert.True(okCount <= 5, sprintf "expected at most 5 OKs (PermitLimit=5), got %d" okCount)
        Assert.True(rejectedCount > 0, "expected at least one 429")
        Assert.False(String.IsNullOrEmpty retryAfter, "Retry-After header missing on 429")
    }

[<Collection("RateLimitAndCacheDefault")>]
type RateLimitAndCacheCacheTests(fixture: RateLimitAndCacheDefaultFixture) =

    [<Fact>]
    [<Trait("Category", "RateLimitAndCache")>]
    [<Trait("Category", "Integration")>]
    member _.``GET lot caches result and second call hits cache``() = task {
        use client = newClient fixture.Port
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (createLotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! firstResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)
        let firstHeader = firstResp.Headers |> Seq.find (fun h -> h.Key = "X-Cache")
        Assert.Equal("MISS", firstHeader.Value |> Seq.head)

        let! secondResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode)
        let secondHeader = secondResp.Headers |> Seq.find (fun h -> h.Key = "X-Cache")
        Assert.Equal("HIT", secondHeader.Value |> Seq.head)
    }

    [<Fact>]
    [<Trait("Category", "RateLimitAndCache")>]
    [<Trait("Category", "Integration")>]
    member _.``cache is invalidated after state-changing transition``() = task {
        use client = newClient fixture.Port
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (createLotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        // Prime cache
        let! _ = getReq client (sprintf "/lots/%s" lotId)
        let! cachedResp = getReq client (sprintf "/lots/%s" lotId)
        let cachedHeader = cachedResp.Headers |> Seq.find (fun h -> h.Key = "X-Cache")
        Assert.Equal("HIT", cachedHeader.Value |> Seq.head)

        // Mutate
        let! mutateResp =
            postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) """{"date":"2026-04-22","version":1}"""

        Assert.Equal(HttpStatusCode.OK, mutateResp.StatusCode)

        // After mutation, the cache for this lot should be gone
        let! afterResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, afterResp.StatusCode)
        let afterHeader = afterResp.Headers |> Seq.find (fun h -> h.Key = "X-Cache")
        Assert.Equal("MISS", afterHeader.Value |> Seq.head)

        let! body = afterResp.Content.ReadAsStringAsync()
        Assert.Contains("\"status\":\"manufactured\"", body)
    }

[<Collection("RateLimitAndCacheShutdown")>]
type RateLimitAndCacheGracefulShutdownTests(fixture: RateLimitAndCacheShutdownFixture) =

    // Run last; this test triggers app shutdown.
    [<Fact>]
    [<Trait("Category", "RateLimitAndCache")>]
    [<Trait("Category", "Integration")>]
    [<Trait("Category", "ZZShutdown")>]
    member _.``graceful shutdown waits for in-flight requests``() = task {
        use client = newClient fixture.Port

        let stopwatch = Stopwatch.StartNew()
        // Warm-up: ensure server is responding.
        let! warm = getReq client "/test/slow?ms=0"
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode)

        // Send a slow in-flight request.
        let slowTask = getReq client "/test/slow?ms=2000"

        // Give the request time to reach the server.
        do! Task.Delay 300

        // Begin graceful shutdown.
        let stopTask = fixture.App.StopAsync()

        // The slow request should still complete with 200.
        let! resp = slowTask
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        do! stopTask
        stopwatch.Stop()

        // The whole sequence should have taken at least the slow delay.
        Assert.True(
            stopwatch.ElapsedMilliseconds >= 1500L,
            sprintf "expected >=1500ms (slow request waited), got %dms" stopwatch.ElapsedMilliseconds
        )
    }
