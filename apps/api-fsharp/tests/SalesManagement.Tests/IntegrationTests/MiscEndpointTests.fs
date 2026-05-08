module SalesManagement.Tests.IntegrationTests.MiscEndpointTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Npgsql
open Xunit
open SalesManagement.Hosting
open SalesManagement.Domain.Events
open SalesManagement.Infrastructure
open SalesManagement.Api.LotRoutes
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.StandaloneAppHost

// ----------------------------------------------------------------------------
// F20-1: /test/slow gating
// ----------------------------------------------------------------------------

[<Trait("Category", "MiscEndpoint")>]
[<Trait("Category", "Integration")>]
type SlowEndpointTests() =

    let buildSlowApp (env: string) : Task<WebApplication * int> = task {
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

    let buildClient (port: int) : HttpClient = newClient port

    [<Fact>]
    member _.``test/slow returns 404 in Production``() = task {
        let! app, port = buildSlowApp "Production"

        try
            use client = buildClient port
            let! resp = client.GetAsync "/test/slow"
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        finally
            app.StopAsync().GetAwaiter().GetResult()
    }

    [<Fact>]
    member _.``test/slow rejects ms out of bounds in Development``() = task {
        let! app, port = buildSlowApp "Development"

        try
            use client = buildClient port
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
// and FOR UPDATE SKIP LOCKED behaviour). Uses a slow outbox poll so the
// OutboxConcurrency test can drive its own processors without contention.
// ----------------------------------------------------------------------------

type MiscDbFixture() =
    inherit
        ApiFixture(
            { defaultOptions with
                AuthEnabled = false
                ExtraArgs = [ "--Outbox:PollIntervalMs=60000" ] }
        )

[<CollectionDefinition("MiscEndpointDb")>]
type MiscDbCollection() =
    interface ICollectionFixture<MiscDbFixture>

[<Collection("MiscEndpointDb")>]
type CreateLotErrorHandlingTests(fixture: MiscDbFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "MiscEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``createLot returns 400 for malformed JSON``() = task {
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" "{ broken"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "MiscEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``createLot returns 409 with safe body on duplicate``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let location = sprintf "DUPF20%d" (r.Next(0, 9999))

        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt 2099)
                    Location = Some(JString location)
                    Seq = Some(JInt 1) }

        let! firstResp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)
        let! dupResp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode)

        let! dupBody = readBody dupResp
        Assert.Contains("duplicate-resource", dupBody)
        // Must not leak the bare DB driver text or the SqlState code.
        Assert.DoesNotContain("23505", dupBody)
        Assert.DoesNotContain("duplicate key value violates", dupBody)
    }

[<Collection("MiscEndpointDb")>]
type OutboxConcurrencyTests(fixture: MiscDbFixture) =
    do fixture.Reset()

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
