module SalesManagement.Tests.IntegrationTests.ConfigurationTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Configuration
open Xunit
open SalesManagement.Hosting

let private fsharpRoot =
    // base dir = tests/SalesManagement.Tests/bin/Debug/net10.0 → up 5 → fsharp/
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."))

let private appsettingsPath =
    Path.Combine(fsharpRoot, "src", "SalesManagement", "appsettings.json")

let private programFsPath =
    Path.Combine(fsharpRoot, "src", "SalesManagement", "Program.fs")

[<Fact>]
[<Trait("Category", "Configuration")>]
let ``appsettings.json defines Database.ConnectionString and Server.Port`` () =
    Assert.True(File.Exists appsettingsPath, sprintf "appsettings.json not found at %s" appsettingsPath)
    let json = File.ReadAllText appsettingsPath
    use doc = JsonDocument.Parse json
    let root = doc.RootElement

    let dbProp = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty("Database", dbProp), "Database section missing")
    let csProp = ref Unchecked.defaultof<JsonElement>
    Assert.True((!dbProp).TryGetProperty("ConnectionString", csProp), "Database.ConnectionString missing")
    Assert.False(String.IsNullOrWhiteSpace((!csProp).GetString()), "Database.ConnectionString must not be empty")

    let srvProp = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty("Server", srvProp), "Server section missing")
    let portProp = ref Unchecked.defaultof<JsonElement>
    Assert.True((!srvProp).TryGetProperty("Port", portProp), "Server.Port missing")
    Assert.True((!portProp).GetInt32() > 0, "Server.Port must be a positive integer")

[<Fact>]
[<Trait("Category", "Configuration")>]
let ``environment variable overrides Database.ConnectionString from appsettings.json`` () =
    let envKey = "Database__ConnectionString"
    let original = Environment.GetEnvironmentVariable envKey

    try
        let overridden =
            "Host=override-host;Port=6543;Database=override;Username=u;Password=p"

        Environment.SetEnvironmentVariable(envKey, overridden)

        let config =
            ConfigurationBuilder().AddJsonFile(appsettingsPath, optional = false).AddEnvironmentVariables().Build()

        Assert.Equal(overridden, config.["Database:ConnectionString"])
    finally
        Environment.SetEnvironmentVariable(envKey, original)

[<Fact>]
[<Trait("Category", "Configuration")>]
let ``environment variable overrides Server.Port from appsettings.json`` () =
    let envKey = "Server__Port"
    let original = Environment.GetEnvironmentVariable envKey

    try
        Environment.SetEnvironmentVariable(envKey, "9090")

        let config =
            ConfigurationBuilder().AddJsonFile(appsettingsPath, optional = false).AddEnvironmentVariables().Build()

        Assert.Equal("9090", config.["Server:Port"])
    finally
        Environment.SetEnvironmentVariable(envKey, original)

[<Fact>]
[<Trait("Category", "Configuration")>]
let ``Program.fs has no hardcoded production connection string fallback`` () =
    Assert.True(File.Exists programFsPath, sprintf "Program.fs not found at %s" programFsPath)
    let text = File.ReadAllText programFsPath
    Assert.DoesNotContain("Host=localhost;Port=5432;Database=sales_management", text)
    Assert.DoesNotContain("Username=app;Password=app", text)

[<Fact>]
[<Trait("Category", "Configuration")>]
let ``http requests emit JSON logs with timestamp/level/message/requestId and IDs differ`` () = task {
    let port =
        let listener = new TcpListener(IPAddress.Loopback, 0)
        listener.Start()
        let p = (listener.LocalEndpoint :?> IPEndPoint).Port
        listener.Stop()
        p

    let connKey = "Database__ConnectionString"
    let originalConn = Environment.GetEnvironmentVariable connKey
    // Use a placeholder connection string so createApp doesn't fail when DB env is missing.
    if String.IsNullOrEmpty originalConn then
        Environment.SetEnvironmentVariable(connKey, "Host=localhost;Port=5432;Database=sm;Username=u;Password=p")

    let originalOut = Console.Out
    let captured = new StringWriter()
    Console.SetOut(captured)

    try
        let args =
            [| sprintf "--Server:Port=%d" port; "--Logging:LogLevel:Default=Information" |]

        let app = createApp args
        do! app.StartAsync()

        try
            use client = new HttpClient()
            client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)

            // /openapi.yaml is auth-free and does not require a live DB, so it works as
            // a simple endpoint for exercising the request-logging pipeline.
            let! r1 = client.GetAsync("/openapi.yaml")
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode)
            let! r2 = client.GetAsync("/openapi.yaml")
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode)

            // give Serilog time to flush
            do! Task.Delay(800)
            Serilog.Log.CloseAndFlush()
        finally
            app.StopAsync().GetAwaiter().GetResult()
    finally
        Console.SetOut(originalOut)

        if String.IsNullOrEmpty originalConn then
            Environment.SetEnvironmentVariable(connKey, null)

    let raw = captured.ToString()

    let lines =
        raw.Split([| '\n'; '\r' |], StringSplitOptions.RemoveEmptyEntries)
        |> Array.filter (fun l -> not (String.IsNullOrWhiteSpace l))

    Assert.NotEmpty(lines)

    let parsed =
        lines
        |> Array.choose (fun line ->
            let trimmed = line.Trim()

            if trimmed.StartsWith "{" then
                try
                    Some(JsonDocument.Parse trimmed)
                with _ ->
                    None
            else
                None)

    Assert.NotEmpty(parsed)

    // every JSON line must have timestamp, level, message
    for doc in parsed do
        let root = doc.RootElement
        Assert.Equal(JsonValueKind.Object, root.ValueKind)
        let ts = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("timestamp", ts), sprintf "timestamp missing in: %s" (root.GetRawText()))
        let lvl = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("level", lvl), sprintf "level missing in: %s" (root.GetRawText()))
        let msg = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("message", msg), sprintf "message missing in: %s" (root.GetRawText()))

    // collect request log lines (HTTP request completion logs from UseSerilogRequestLogging)
    let requestLogs =
        parsed
        |> Array.filter (fun doc ->
            let msg = ref Unchecked.defaultof<JsonElement>

            if doc.RootElement.TryGetProperty("message", msg) then
                let text = (!msg).GetString()
                text.Contains("HTTP")
            else
                false)

    Assert.True(
        requestLogs.Length >= 2,
        sprintf "expected ≥ 2 request logs, got %d. Raw output:\n%s" requestLogs.Length raw
    )

    // each request log must include requestId
    let requestIds =
        requestLogs
        |> Array.choose (fun doc ->
            let rid = ref Unchecked.defaultof<JsonElement>

            if doc.RootElement.TryGetProperty("requestId", rid) then
                Some((!rid).GetString())
            else
                None)

    Assert.True(
        requestIds.Length >= 2,
        sprintf "expected ≥ 2 logs with requestId, got %d. Raw output:\n%s" requestIds.Length raw
    )

    let distinct = requestIds |> Array.distinct
    Assert.True(distinct.Length >= 2, sprintf "expected distinct requestIds, got %A" requestIds)
}
