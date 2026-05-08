module SalesManagement.Tests.IntegrationTests.AuthConfigEndpointTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.StandaloneAppHost

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

let private buildArgs (extraArgs: string array) (port: int) : string array =
    Array.append (commonArgs port) extraArgs

type AuthConfigEndpointAuthOffFixture() =
    let host = StandaloneApp()

    member _.Port = host.Port
    member _.NewClient() = host.NewClient()

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            host.Start(
                buildArgs
                    [| "--Authentication:Enabled=false"
                       "--Authentication:Authority=http://localhost:8180/realms/sales-management"
                       "--Authentication:Audience=sales-api" |]
            )

        member _.DisposeAsync() : Task = host.Stop()

[<CollectionDefinition("AuthConfigEndpointAuthOff")>]
type AuthConfigEndpointAuthOffCollection() =
    interface ICollectionFixture<AuthConfigEndpointAuthOffFixture>

type AuthConfigEndpointAuthOnFixture() =
    let host = StandaloneApp()

    member _.Port = host.Port
    member _.NewClient() = host.NewClient()

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            host.Start(
                buildArgs
                    [| "--Authentication:Enabled=true"
                       "--Authentication:Authority=https://idp.example.com/realms/sales"
                       "--Authentication:Audience=sales-api"
                       "--Authentication:SigningKey=stepf17-test-key-please-do-not-use-in-production-context"
                       "--Authentication:RequireHttpsMetadata=false" |]
            )

        member _.DisposeAsync() : Task = host.Stop()

[<CollectionDefinition("AuthConfigEndpointAuthOn")>]
type AuthConfigEndpointAuthOnCollection() =
    interface ICollectionFixture<AuthConfigEndpointAuthOnFixture>

let private fetchAuthConfig (client: HttpClient) : Task<HttpResponseMessage * JsonElement> = task {
    let! resp = getReq client "/auth/config"
    let! body = readBody resp
    return resp, parseJson body
}

[<Collection("AuthConfigEndpointAuthOff")>]
type AuthOffTests(fixture: AuthConfigEndpointAuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config returns enabled=false only when authentication is disabled``() = task {
        use client = fixture.NewClient()
        let! resp, root = fetchAuthConfig client

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
        use client = fixture.NewClient()
        let! resp = getReq client "/auth/config"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

[<Collection("AuthConfigEndpointAuthOn")>]
type AuthOnTests(fixture: AuthConfigEndpointAuthOnFixture) =

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config returns enabled=true with authority and audience when authentication is enabled``() = task {
        use client = fixture.NewClient()
        let! resp, root = fetchAuthConfig client

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
        use client = fixture.NewClient()
        // No Authorization header attached on purpose.
        let! resp = getReq client "/auth/config"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "AuthConfigEndpoint")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /auth/config does not leak secrets``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/auth/config"
        let! body = readBody resp
        let lower = body.ToLowerInvariant()
        Assert.DoesNotContain("signingkey", lower)
        Assert.DoesNotContain("signing_key", lower)
        Assert.DoesNotContain("secret", lower)
        Assert.DoesNotContain("password", lower)
    }
