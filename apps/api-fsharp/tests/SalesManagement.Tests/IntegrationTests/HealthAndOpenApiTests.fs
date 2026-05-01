module SalesManagement.Tests.IntegrationTests.HealthAndOpenApiTests

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

let private upConnectionString =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

// Unreachable PostgreSQL: 127.0.0.1 with a port unlikely to be open and a short timeout.
let private downConnectionString =
    "Host=127.0.0.1;Port=1;Database=sales_management;Username=app;Password=app;Timeout=2;Command Timeout=2"

let private startApp (connectionString: string) (authEnabled: bool) : WebApplication * int =
    let port = getFreePort ()

    let baseArgs =
        [ sprintf "--Server:Port=%d" port
          sprintf "--Database:ConnectionString=%s" connectionString
          "--Logging:LogLevel:Default=Warning" ]

    let authArgs =
        if authEnabled then
            [ "--Authentication:Enabled=true"
              "--Authentication:SigningKey=step04-test-signing-key-please-do-not-use-in-prod"
              "--Authentication:Audience=sales-api" ]
        else
            []

    let args = baseArgs @ authArgs |> List.toArray
    let app = createApp args
    app.StartAsync().GetAwaiter().GetResult()
    app, port

let private stopApp (app: WebApplication) =
    app.StopAsync().GetAwaiter().GetResult()

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /health returns 200 with status UP when DB is reachable`` () = task {
    let app, port = startApp upConnectionString false

    try
        use client = newClient port
        let! resp = client.GetAsync "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("UP", root.GetProperty("status").GetString())
        let checks = root.GetProperty("checks")
        Assert.Equal("UP", checks.GetProperty("postgresql").GetString())
        Assert.Equal("UP", checks.GetProperty("self").GetString())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /health returns 503 with status DOWN when DB is unreachable`` () = task {
    let app, port = startApp downConnectionString false

    try
        use client = newClient port
        let! resp = client.GetAsync "/health"
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("DOWN", root.GetProperty("status").GetString())
        let checks = root.GetProperty("checks")
        Assert.Equal("DOWN", checks.GetProperty("postgresql").GetString())
        Assert.Equal("UP", checks.GetProperty("self").GetString())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /health does not require authentication even when auth is enabled`` () = task {
    let app, port = startApp upConnectionString true

    try
        use client = newClient port
        let! resp = client.GetAsync "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /openapi.yaml returns the OpenAPI spec`` () = task {
    let app, port = startApp upConnectionString false

    try
        use client = newClient port
        let! resp = client.GetAsync "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        Assert.Contains("openapi:", body)
        Assert.Contains("Sales Management API", body)
        Assert.Contains("/lots", body)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /openapi.yaml does not require authentication`` () = task {
    let app, port = startApp upConnectionString true

    try
        use client = newClient port
        let! resp = client.GetAsync "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /swagger returns Swagger UI HTML`` () = task {
    let app, port = startApp upConnectionString false

    try
        use client = newClient port
        let! resp = client.GetAsync "/swagger"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        Assert.Contains("swagger-ui", body)
        Assert.Contains("/openapi.yaml", body)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "HealthAndOpenApi")>]
let ``GET /swagger does not require authentication even when auth is enabled`` () = task {
    let app, port = startApp upConnectionString true

    try
        use client = newClient port
        let! resp = client.GetAsync "/swagger"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    finally
        stopApp app
}
