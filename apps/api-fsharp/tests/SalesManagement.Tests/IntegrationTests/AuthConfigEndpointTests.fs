module SalesManagement.Tests.IntegrationTests.AuthConfigEndpointTests

open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Xunit
open SalesManagement.Hosting

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private commonArgs (port: int) =
    [| sprintf "--Server:Port=%d" port
       "--Database:ConnectionString=Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x"
       "--RateLimit:PermitLimit=100000"
       "--RateLimit:WindowSeconds=60"
       "--Outbox:PollIntervalMs=60000"
       "--ExternalApi:PricingUrl=http://127.0.0.1:1"
       "--ExternalApi:TimeoutMs=500"
       "--ExternalApi:RetryCount=0"
       "--Logging:LogLevel:Default=Warning" |]

type AuthConfigEndpointAuthOffFixture() =
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0

    member _.Port = port

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                port <- getFreePort ()

                let args =
                    Array.append
                        (commonArgs port)
                        [| "--Authentication:Enabled=false"
                           "--Authentication:Authority=http://localhost:8180/realms/sales-management"
                           "--Authentication:Audience=sales-api" |]

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
            }
            :> Task

[<CollectionDefinition("AuthConfigEndpointAuthOff")>]
type AuthConfigEndpointAuthOffCollection() =
    interface ICollectionFixture<AuthConfigEndpointAuthOffFixture>

type AuthConfigEndpointAuthOnFixture() =
    let mutable app: WebApplication = Unchecked.defaultof<_>
    let mutable port: int = 0

    member _.Port = port

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            task {
                port <- getFreePort ()

                let args =
                    Array.append
                        (commonArgs port)
                        [| "--Authentication:Enabled=true"
                           "--Authentication:Authority=https://idp.example.com/realms/sales"
                           "--Authentication:Audience=sales-api"
                           "--Authentication:SigningKey=stepf17-test-key-please-do-not-use-in-production-context"
                           "--Authentication:RequireHttpsMetadata=false" |]

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
            }
            :> Task

[<CollectionDefinition("AuthConfigEndpointAuthOn")>]
type AuthConfigEndpointAuthOnCollection() =
    interface ICollectionFixture<AuthConfigEndpointAuthOnFixture>

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client.Timeout <- TimeSpan.FromSeconds 30.0
    client

let private getAuthConfig (client: HttpClient) (req: HttpRequestMessage) : Task<HttpResponseMessage * JsonElement> = task {
    let! resp = client.SendAsync req
    let! body = resp.Content.ReadAsStringAsync()
    let doc = JsonDocument.Parse body
    return resp, doc.RootElement.Clone()
}

[<Collection("AuthConfigEndpointAuthOff")>]
type AuthOffTests(fixture: AuthConfigEndpointAuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config returns enabled=false only when authentication is disabled``() = task {
        use client = newClient fixture.Port
        let req = new HttpRequestMessage(HttpMethod.Get, "/auth/config")
        let! resp, root = getAuthConfig client req

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        let mutable enabledEl = Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("enabled", &enabledEl), "enabled property must exist")
        Assert.Equal(JsonValueKind.False, enabledEl.ValueKind)

        let mutable scratch = Unchecked.defaultof<JsonElement>
        Assert.False(root.TryGetProperty("authority", &scratch), "authority must be absent when disabled")
        Assert.False(root.TryGetProperty("audience", &scratch), "audience must be absent when disabled")
    }

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config is reachable without Authorization header``() = task {
        use client = newClient fixture.Port
        let req = new HttpRequestMessage(HttpMethod.Get, "/auth/config")
        let! resp = client.SendAsync req
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

[<Collection("AuthConfigEndpointAuthOn")>]
type AuthOnTests(fixture: AuthConfigEndpointAuthOnFixture) =

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config returns enabled=true with authority and audience when authentication is enabled``() = task {
        use client = newClient fixture.Port
        let req = new HttpRequestMessage(HttpMethod.Get, "/auth/config")
        let! resp, root = getAuthConfig client req

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        let mutable enabledEl = Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("enabled", &enabledEl), "enabled property must exist")
        Assert.Equal(JsonValueKind.True, enabledEl.ValueKind)

        let mutable authorityEl = Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("authority", &authorityEl), "authority must be present when enabled")
        Assert.Equal("https://idp.example.com/realms/sales", authorityEl.GetString())

        let mutable audienceEl = Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("audience", &audienceEl), "audience must be present when enabled")
        Assert.Equal("sales-api", audienceEl.GetString())
    }

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config does not require a Bearer token even when authentication is enabled``() = task {
        use client = newClient fixture.Port
        // No Authorization header attached on purpose.
        let req = new HttpRequestMessage(HttpMethod.Get, "/auth/config")
        let! resp = client.SendAsync req
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config does not leak secrets``() = task {
        use client = newClient fixture.Port
        let req = new HttpRequestMessage(HttpMethod.Get, "/auth/config")
        let! resp = client.SendAsync req
        let! body = resp.Content.ReadAsStringAsync()
        let lower = body.ToLowerInvariant()
        Assert.DoesNotContain("signingkey", lower)
        Assert.DoesNotContain("signing_key", lower)
        Assert.DoesNotContain("secret", lower)
        Assert.DoesNotContain("password", lower)
    }
