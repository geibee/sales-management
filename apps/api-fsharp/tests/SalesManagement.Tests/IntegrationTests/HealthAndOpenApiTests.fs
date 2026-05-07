module SalesManagement.Tests.IntegrationTests.HealthAndOpenApiTests

open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.StandaloneAppHost

/// /health の DOWN 経路を確認するための専用 fixture。
/// `Database:ConnectionString` を到達不能ホストに向けるため、testcontainer は使わない。
type HealthDownFixture() =
    let host = StandaloneApp()

    member _.Port = host.Port

    member _.NewClient() : HttpClient = host.NewClient()

    interface IAsyncLifetime with
        member _.InitializeAsync() : Task =
            host.Start(fun port ->
                [| sprintf "--Server:Port=%d" port
                   "--Database:ConnectionString=Host=127.0.0.1;Port=1;Database=sales_management;Username=app;Password=app;Timeout=2;Command Timeout=2"
                   "--Authentication:Enabled=false"
                   "--Logging:LogLevel:Default=Warning" |])

        member _.DisposeAsync() : Task = host.Stop()

[<CollectionDefinition("HealthDown")>]
type HealthDownCollection() =
    interface ICollectionFixture<HealthDownFixture>


[<Collection("ApiAuthOff")>]
type HealthAndOpenApiAuthOffTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /health returns 200 with status UP when DB is reachable``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("UP", root.GetProperty("status").GetString())
        let checks = root.GetProperty("checks")
        Assert.Equal("UP", checks.GetProperty("postgresql").GetString())
        Assert.Equal("UP", checks.GetProperty("self").GetString())
    }

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /openapi.yaml returns the OpenAPI spec``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("openapi:", body)
        Assert.Contains("Sales Management API", body)
        Assert.Contains("/lots", body)
    }

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /swagger returns Swagger UI HTML``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/swagger"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("swagger-ui", body)
        Assert.Contains("/openapi.yaml", body)
    }


[<Collection("ApiAuthOn")>]
type HealthAndOpenApiAuthOnTests(fixture: AuthOnFixture) =

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /health does not require authentication even when auth is enabled``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /openapi.yaml does not require authentication``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /swagger does not require authentication even when auth is enabled``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/swagger"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }


[<Collection("HealthDown")>]
type HealthAndOpenApiDownTests(fixture: HealthDownFixture) =

    [<Fact>]
    [<Trait("Category", "HealthAndOpenApi")>]
    member _.``GET /health returns 503 with status DOWN when DB is unreachable``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("DOWN", root.GetProperty("status").GetString())
        let checks = root.GetProperty("checks")
        Assert.Equal("DOWN", checks.GetProperty("postgresql").GetString())
        Assert.Equal("UP", checks.GetProperty("self").GetString())
    }
