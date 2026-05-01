module SalesManagement.Tests.IntegrationTests.MiscEndpointTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text
open System.Threading
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Npgsql
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting
open SalesManagement.Domain.Events
open SalesManagement.Infrastructure
open SalesManagement.Api.LotRoutes

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

// ----------------------------------------------------------------------------
// F20-1: /test/slow gating
// ----------------------------------------------------------------------------

let private buildSlowApp (env: string) : Task<WebApplication * int> = task {
    let port = getFreePort ()

    let args =
        [| sprintf "--Server:Port=%d" port
           "--Database:ConnectionString=Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x"
           "--Authentication:Enabled=false"
           "--RateLimit:PermitLimit=100000"
           "--RateLimit:WindowSeconds=60"
           "--Outbox:PollIntervalMs=60000"
           "--ExternalApi:PricingUrl=http://127.0.0.1:1"
           "--ExternalApi:TimeoutMs=500"
           "--ExternalApi:RetryCount=0"
           "--Logging:LogLevel:Default=Warning"
           sprintf "--environment=%s" env |]

    let app = createApp args
    do! app.StartAsync()
    return app, port
}

[<Trait("Category", "MiscEndpoint")>]
[<Trait("Category", "Integration")>]
type SlowEndpointTests() =

    [<Fact>]
    member _.``test/slow returns 404 in Production``() = task {
        let! app, port = buildSlowApp "Production"

        try
            use client = new HttpClient()
            client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
            let! resp = client.GetAsync "/test/slow"
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        finally
            app.StopAsync().GetAwaiter().GetResult()
    }

    [<Fact>]
    member _.``test/slow rejects ms out of bounds in Development``() = task {
        let! app, port = buildSlowApp "Development"

        try
            use client = new HttpClient()
            client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
            client.Timeout <- TimeSpan.FromSeconds 10.0

            let! negResp = client.GetAsync "/test/slow?ms=-1"
            Assert.Equal(HttpStatusCode.BadRequest, negResp.StatusCode)

            let! tooBigResp = client.GetAsync "/test/slow?ms=99999999"
            Assert.Equal(HttpStatusCode.BadRequest, tooBigResp.StatusCode)

            let! okResp = client.GetAsync "/test/slow?ms=100"
            Assert.Equal(HttpStatusCode.OK, okResp.StatusCode)
        finally
            app.StopAsync().GetAwaiter().GetResult()
    }

// ----------------------------------------------------------------------------
// F20-4: cache key canonicalization
// ----------------------------------------------------------------------------

[<Trait("Category", "MiscEndpoint")>]
type LotCacheCanonicalizationTests() =

    [<Fact>]
    member _.``cacheKey produces same string for short and zero-padded forms``() =
        let short =
            match SalesManagement.Domain.Types.LotNumber.tryParse "2099-Q-7" with
            | Some n -> n
            | None -> failwith "tryParse 2099-Q-7"

        let padded =
            match SalesManagement.Domain.Types.LotNumber.tryParse "2099-Q-007" with
            | Some n -> n
            | None -> failwith "tryParse 2099-Q-007"

        Assert.Equal(cacheKey short, cacheKey padded)

// ----------------------------------------------------------------------------
// F20-2 / F20-3: shared Postgres+app fixture (real Npgsql for SqlState 23505
// and FOR UPDATE SKIP LOCKED behaviour).
// ----------------------------------------------------------------------------

type MiscDbFixture() =
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
                       "--Outbox:PollIntervalMs=60000"
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

[<CollectionDefinition("MiscEndpointDb")>]
type MiscDbCollection() =
    interface ICollectionFixture<MiscDbFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 60.0
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
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

[<Collection("MiscEndpointDb")>]
type CreateLotErrorHandlingTests(fixture: MiscDbFixture) =

    [<Fact>]
    [<Trait("Category", "MiscEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``createLot returns 400 for malformed JSON``() = task {
        use client = newClient fixture.Port
        let! resp = postJson client "/lots" "{ broken"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "MiscEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``createLot returns 409 with safe body on duplicate``() = task {
        use client = newClient fixture.Port
        let r = Random()
        let year = 2099
        let location = sprintf "DUPF20%d" (r.Next(0, 9999))
        let body = createLotBody year location 1
        let! firstResp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)
        let! dupResp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode)

        let! dupBody = dupResp.Content.ReadAsStringAsync()
        Assert.Contains("duplicate-resource", dupBody)
        // Must not leak the bare DB driver text or the SqlState code.
        Assert.DoesNotContain("23505", dupBody)
        Assert.DoesNotContain("duplicate key value violates", dupBody)
    }

[<Collection("MiscEndpointDb")>]
type OutboxConcurrencyTests(fixture: MiscDbFixture) =

    let insertPending (count: int) =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()

        for i in 1..count do
            use cmd = conn.CreateCommand()

            cmd.CommandText <- "INSERT INTO outbox_events (event_type, payload) VALUES (@t, @p::jsonb)"

            let p1 = cmd.CreateParameter()
            p1.ParameterName <- "t"
            p1.Value <- "LotManufacturingCompleted"
            cmd.Parameters.Add p1 |> ignore
            let p2 = cmd.CreateParameter()
            p2.ParameterName <- "p"

            p2.Value <- sprintf """{"lotId":"OBX-F20-%03d","date":"2026-04-01"}""" i

            cmd.Parameters.Add p2 |> ignore
            cmd.ExecuteNonQuery() |> ignore

    let resetOutbox () =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM outbox_events"
        cmd.ExecuteNonQuery() |> ignore

    [<Fact>]
    [<Trait("Category", "MiscEndpoint")>]
    [<Trait("Category", "OutboxConcurrency")>]
    [<Trait("Category", "Integration")>]
    member _.``OutboxConcurrency two processors do not double-publish``() = task {
        resetOutbox ()
        let n = 10
        insertPending n

        let busA = EventBus()
        let busB = EventBus()
        let publishedA = ResizeArray<string>()
        let publishedB = ResizeArray<string>()

        let lockA = obj ()
        let lockB = obj ()

        let recordTo (xs: ResizeArray<string>) (lck: obj) =
            fun (evt: DomainEvent) ->
                match evt with
                | LotManufacturingCompleted(lotId, _) -> lock lck (fun () -> xs.Add lotId)

        busA.Subscribe(recordTo publishedA lockA)
        busB.Subscribe(recordTo publishedB lockB)

        let procA = OutboxProcessor.OutboxProcessor(fixture.ConnectionString, busA, 50)
        let procB = OutboxProcessor.OutboxProcessor(fixture.ConnectionString, busB, 50)

        do! (procA :> Microsoft.Extensions.Hosting.IHostedService).StartAsync(CancellationToken.None)
        do! (procB :> Microsoft.Extensions.Hosting.IHostedService).StartAsync(CancellationToken.None)

        // Wait for the events to drain.
        let deadline = DateTime.UtcNow.AddSeconds 30.0
        let mutable processed = 0

        while processed < n && DateTime.UtcNow < deadline do
            do! Task.Delay 100

            use conn = new NpgsqlConnection(fixture.ConnectionString)
            conn.Open()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "SELECT COUNT(*) FROM outbox_events WHERE status = 'processed'"
            processed <- Convert.ToInt32(cmd.ExecuteScalar())

        do! (procA :> Microsoft.Extensions.Hosting.IHostedService).StopAsync(CancellationToken.None)
        do! (procB :> Microsoft.Extensions.Hosting.IHostedService).StopAsync(CancellationToken.None)

        let setA = Set.ofSeq publishedA
        let setB = Set.ofSeq publishedB
        let union = Set.union setA setB
        let intersection = Set.intersect setA setB

        Assert.Equal(n, processed)
        Assert.Equal(n, union.Count)
        Assert.Empty intersection
        Assert.Equal(publishedA.Count, setA.Count)
        Assert.Equal(publishedB.Count, setB.Count)
    }
