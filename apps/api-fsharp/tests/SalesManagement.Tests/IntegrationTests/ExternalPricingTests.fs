module SalesManagement.Tests.IntegrationTests.ExternalPricingTests

open System
open System.IdentityModel.Tokens.Jwt
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.IdentityModel.Tokens
open WireMock.RequestBuilders
open WireMock.ResponseBuilders
open WireMock.Server
open Xunit
open SalesManagement.Hosting
open SalesManagement.Tests.Support.HttpHelpers

let private signingKey = "step06-test-signing-key-please-do-not-use-in-production"
let private audience = "sales-api"

let private mintToken (roles: string list) : string =
    let keyBytes = Encoding.UTF8.GetBytes signingKey
    let key = SymmetricSecurityKey keyBytes
    let creds = SigningCredentials(key, SecurityAlgorithms.HmacSha256)

    let realmAccess =
        let payload =
            sprintf """{"roles":[%s]}""" (roles |> List.map (sprintf "\"%s\"") |> String.concat ",")

        Claim("realm_access", payload, JsonClaimValueTypes.Json)

    let claims =
        [| Claim("sub", Guid.NewGuid().ToString())
           Claim("preferred_username", roles |> List.tryHead |> Option.defaultValue "anonymous")
           realmAccess |]

    let token =
        JwtSecurityToken(
            issuer = "step06-test",
            audience = audience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow.AddSeconds 300.0),
            signingCredentials = creds
        )

    JwtSecurityTokenHandler().WriteToken token

let private buildArgs (port: int) (wiremockUrl: string) (circuitFailures: int) (retryCount: int) =
    [| sprintf "--Server:Port=%d" port
       "--Database:ConnectionString=Host=localhost;Port=1;Database=unused;Username=u;Password=p"
       "--Authentication:Enabled=true"
       sprintf "--Authentication:SigningKey=%s" signingKey
       sprintf "--Authentication:Audience=%s" audience
       sprintf "--ExternalApi:PricingUrl=%s" wiremockUrl
       "--ExternalApi:TimeoutMs=3000"
       sprintf "--ExternalApi:RetryCount=%d" retryCount
       sprintf "--ExternalApi:CircuitFailures=%d" circuitFailures
       "--Logging:LogLevel:Default=Warning" |]

let private setupBaseStubs (wiremock: WireMockServer) =
    wiremock
        .Given(Request.Create().WithPath("/api/pricing/2024-A-001").UsingGet())
        .RespondWith(
            Response
                .Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"basePrice":10000,"adjustmentRate":1.05,"source":"external-pricing-api"}""")
        )

    wiremock
        .Given(Request.Create().WithPath("/api/pricing/2024-E-001").UsingGet())
        .RespondWith(
            Response
                .Create()
                .WithStatusCode(500)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"Internal Server Error"}""")
        )

    wiremock
        .Given(Request.Create().WithPath("/api/pricing/2024-S-001").UsingGet())
        .RespondWith(
            Response
                .Create()
                .WithStatusCode(200)
                .WithDelay(5000)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"basePrice":10000}""")
        )

let private freePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

type ExternalPricingHappyFixture() =
    let mutable wiremock: WireMockServer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable wiremockUrl: string = ""

    member _.Port = port
    member _.WireMock = wiremock

    member _.NewClient() : HttpClient =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
        client.Timeout <- TimeSpan.FromSeconds 30.0
        client

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                wiremock <- WireMockServer.Start()
                wiremockUrl <- wiremock.Url
                setupBaseStubs wiremock
                port <- freePort ()
                // Very high CircuitFailures so the breaker doesn't open during happy-path tests
                let args = buildArgs port wiremockUrl 1000 2
                app <- createApp args
                do! app.StartAsync()
            }
            :> Task

        member _.DisposeAsync() : Task =
            task {
                if not (isNull (box app)) then
                    do! app.StopAsync()

                if not (isNull (box wiremock)) then
                    wiremock.Stop()
                    (wiremock :> IDisposable).Dispose()
            }
            :> Task

type ExternalPricingCircuitFixture() =
    let mutable wiremock: WireMockServer = Unchecked.defaultof<_>
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0
    let mutable wiremockUrl: string = ""

    member _.Port = port
    member _.WireMock = wiremock

    member _.NewClient() : HttpClient =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
        client.Timeout <- TimeSpan.FromSeconds 30.0
        client

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                wiremock <- WireMockServer.Start()
                wiremockUrl <- wiremock.Url
                setupBaseStubs wiremock
                port <- freePort ()
                // Low circuit threshold and zero retries so the breaker opens deterministically
                let args = buildArgs port wiremockUrl 3 0
                app <- createApp args
                do! app.StartAsync()
            }
            :> Task

        member _.DisposeAsync() : Task =
            task {
                if not (isNull (box app)) then
                    do! app.StopAsync()

                if not (isNull (box wiremock)) then
                    wiremock.Stop()
                    (wiremock :> IDisposable).Dispose()
            }
            :> Task

[<CollectionDefinition("ExternalPricingHappy")>]
type ExternalPricingHappyCollection() =
    interface ICollectionFixture<ExternalPricingHappyFixture>

[<CollectionDefinition("ExternalPricingCircuit")>]
type ExternalPricingCircuitCollection() =
    interface ICollectionFixture<ExternalPricingCircuitFixture>

[<Collection("ExternalPricingHappy")>]
type ExternalPricingHappyTests(fixture: ExternalPricingHappyFixture) =

    [<Fact>]
    [<Trait("Category", "ExternalPricing")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /api/external/price-check returns external pricing JSON``() = task {
        use client = fixture.NewClient()
        let token = mintToken [ "viewer" ]
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let! resp = getReq client "/api/external/price-check?lotId=2024-A-001"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal(10000m, root.GetProperty("basePrice").GetDecimal())
        Assert.Equal("external-pricing-api", root.GetProperty("source").GetString())
    }

    [<Fact>]
    [<Trait("Category", "ExternalPricing")>]
    [<Trait("Category", "Integration")>]
    member _.``retry recovers when first attempt is 500 then 200``() = task {
        // Use a fresh app + custom HttpListener-based upstream so we can deterministically
        // count attempts without relying on WireMock scenario semantics.
        let upstreamPort = freePort ()
        let upstreamUrl = sprintf "http://127.0.0.1:%d" upstreamPort
        let listener = new System.Net.HttpListener()
        listener.Prefixes.Add(sprintf "%s/" upstreamUrl)
        listener.Start()
        let counter = ref 0

        let serve () : Task = task {
            while listener.IsListening do
                try
                    let! ctx = listener.GetContextAsync()
                    let n = System.Threading.Interlocked.Increment(counter)

                    if n = 1 then
                        ctx.Response.StatusCode <- 500
                    else
                        ctx.Response.StatusCode <- 200
                        ctx.Response.ContentType <- "application/json"

                        let body =
                            Encoding.UTF8.GetBytes("""{"basePrice":10000,"source":"external-pricing-api"}""")

                        ctx.Response.ContentLength64 <- int64 body.Length
                        do! ctx.Response.OutputStream.WriteAsync(body, 0, body.Length)

                    ctx.Response.Close()
                with _ ->
                    ()
        }

        let serverTask = serve ()

        try
            let appPort = freePort ()

            let args =
                buildArgs appPort upstreamUrl 1000 2
                |> Array.map (fun a ->
                    if a.StartsWith("--Server:Port=") then
                        sprintf "--Server:Port=%d" appPort
                    else
                        a)

            use app = createApp args
            do! app.StartAsync()

            use client =
                new HttpClient(BaseAddress = Uri(sprintf "http://127.0.0.1:%d" appPort))

            client.Timeout <- TimeSpan.FromSeconds 30.0
            let token = mintToken [ "viewer" ]
            client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
            let! resp = getReq client "/api/external/price-check?lotId=2024-A-002"

            Assert.True(
                !counter >= 2,
                sprintf "Expected at least 2 upstream calls (initial + retry) but got %d" !counter
            )

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
            do! app.StopAsync()
        finally
            listener.Stop()
            (listener :> IDisposable).Dispose()
            serverTask.Wait(100) |> ignore
    }

    [<Fact>]
    [<Trait("Category", "ExternalPricing")>]
    [<Trait("Category", "Integration")>]
    member _.``slow upstream causes 502 timeout``() = task {
        use client = fixture.NewClient()
        let token = mintToken [ "viewer" ]
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let! resp = getReq client "/api/external/price-check?lotId=2024-S-001"
        Assert.Equal(HttpStatusCode.BadGateway, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("external-service-error", root.GetProperty("type").GetString())
    }

    [<Fact>]
    [<Trait("Category", "ExternalPricing")>]
    [<Trait("Category", "Integration")>]
    member _.``request without token returns 401``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/api/external/price-check?lotId=2024-A-001"
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)
    }

[<Collection("ExternalPricingCircuit")>]
type ExternalPricingCircuitTests(fixture: ExternalPricingCircuitFixture) =

    [<Fact>]
    [<Trait("Category", "ExternalPricing")>]
    [<Trait("Category", "Integration")>]
    member _.``circuit breaker opens after consecutive failures``() = task {
        use client = fixture.NewClient()
        let token = mintToken [ "viewer" ]
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)

        // Threshold = 3 failures with 0 retries → 3 calls to trip the breaker.
        for _ in 1..3 do
            let! _ = getReq client "/api/external/price-check?lotId=2024-E-001"
            ()

        // Next call should hit the open breaker.
        let! resp = getReq client "/api/external/price-check?lotId=2024-E-001"
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("external-service-unavailable", root.GetProperty("type").GetString())
    }
