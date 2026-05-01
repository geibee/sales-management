module SalesManagement.Tests.IntegrationTests.LotErrorHandlingTests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Hosting

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private connectionString =
    match Environment.GetEnvironmentVariable("DATABASE_URL") with
    | null
    | "" -> "Host=localhost;Port=5432;Database=sales_management;Username=app;Password=app"
    | url -> url

let private startApp () : Microsoft.AspNetCore.Builder.WebApplication * int =
    let port = getFreePort ()

    let args =
        [| sprintf "--Server:Port=%d" port
           sprintf "--Database:ConnectionString=%s" connectionString
           "--Logging:LogLevel:Default=Warning" |]

    let app = createApp args
    app.StartAsync().GetAwaiter().GetResult()
    app, port

let private stopApp (app: Microsoft.AspNetCore.Builder.WebApplication) =
    app.StopAsync().GetAwaiter().GetResult()

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client

let private postJson (client: HttpClient) (path: string) (body: string) : Task<HttpResponseMessage> =
    let content = new StringContent(body, Encoding.UTF8, "application/json")
    client.PostAsync(path, content)

let private uniqueLot () : int * string * int * string =
    // unique year+seq pair to avoid collision across tests
    let r = Random()
    let year = 2050 + r.Next(0, 5000)
    let location = "Z"
    let seq = r.Next(1, 999)
    let id = sprintf "%d-%s-%03d" year location seq
    year, location, seq, id

let private createManufacturingLot (client: HttpClient) : Task<string> = task {
    let year, location, seq, id = uniqueLot ()

    let body =
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

    let! resp = postJson client "/lots" body
    resp.EnsureSuccessStatusCode() |> ignore
    return id
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``GET /lots/{missing} returns 404 problem details`` () = task {
    let app, port = startApp ()

    try
        use client = newClient port
        let! resp = client.GetAsync("/lots/9999-Z-999")
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("not-found", root.GetProperty("type").GetString())
        Assert.Equal("Resource not found", root.GetProperty("title").GetString())
        Assert.Equal(404, root.GetProperty("status").GetInt32())
        Assert.Contains("9999-Z-999", root.GetProperty("detail").GetString())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``POST invalid state transition returns 400 problem details`` () = task {
    let app, port = startApp ()

    try
        use client = newClient port
        let! lotId = createManufacturingLot client
        // Lot is in 'manufacturing' state. Send 'complete-shipping' which requires ShippingInstructed.
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-shipping" lotId) body
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse text
        let root = doc.RootElement
        Assert.Equal("invalid-state-transition", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``POST /lots with multiple invalid fields returns aggregated validation errors`` () = task {
    let app, port = startApp ()

    try
        use client = newClient port

        let body =
            """{
                "lotNumber": {"year": -1, "location": "", "seq": 0},
                "divisionCode": 1, "departmentCode": 1, "sectionCode": 1,
                "processCategory": 1, "inspectionCategory": 1, "manufacturingCategory": 1,
                "details": [
                    {"itemCategory": "general", "premiumCategory": "", "productCategoryCode": "v",
                     "lengthSpecLower": 1.0, "thicknessSpecLower": 1.0, "thicknessSpecUpper": 2.0,
                     "qualityGrade": "A", "count": -5, "quantity": -1.0, "inspectionResultCategory": ""}
                ]
            }"""

        let! resp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse text
        let root = doc.RootElement
        Assert.Equal("validation-error", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
        let errors = root.GetProperty("errors")

        Assert.True(
            errors.GetArrayLength() >= 4,
            sprintf "expected ≥ 4 errors, got %d. body=%s" (errors.GetArrayLength()) text
        )

        let fields =
            [ for e in errors.EnumerateArray() -> e.GetProperty("field").GetString() ]

        Assert.Contains("lotNumber.year", fields)
        Assert.Contains("lotNumber.location", fields)
        Assert.Contains("lotNumber.seq", fields)
        // count or quantity error in details
        let hasDetailError = fields |> List.exists (fun f -> f.StartsWith "details[")
        Assert.True(hasDetailError, sprintf "expected detail errors, got fields=%A" fields)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``GET /lots/{id} response includes version field`` () = task {
    let app, port = startApp ()

    try
        use client = newClient port
        let! lotId = createManufacturingLot client
        let! resp = client.GetAsync(sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        let versionProp = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("version", versionProp), sprintf "version missing in: %s" body)
        Assert.Equal(1, (!versionProp).GetInt32())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``correct version updates lot and increments version; stale version returns 409 conflict`` () = task {
    let app, port = startApp ()

    try
        use client = newClient port
        let! lotId = createManufacturingLot client

        // Update with version=1 → success, new version=2
        let body1 = """{"date": "2026-04-22", "version": 1}"""
        let! resp1 = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body1
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)
        let! text1 = resp1.Content.ReadAsStringAsync()
        use doc1 = JsonDocument.Parse text1
        Assert.Equal("manufactured", doc1.RootElement.GetProperty("status").GetString())
        Assert.Equal(2, doc1.RootElement.GetProperty("version").GetInt32())

        // Update with version=1 again (stale) → 409 Conflict
        let body2 = """{"deadline": "2026-05-01", "version": 1}"""
        let! resp2 = postJson client (sprintf "/lots/%s/instruct-shipping" lotId) body2
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode)
        Assert.Equal("application/problem+json", resp2.Content.Headers.ContentType.MediaType)
        let! text2 = resp2.Content.ReadAsStringAsync()
        use doc2 = JsonDocument.Parse text2
        let root2 = doc2.RootElement
        Assert.Equal("optimistic-lock-conflict", root2.GetProperty("type").GetString())
        Assert.Equal(409, root2.GetProperty("status").GetInt32())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "LotErrorHandling")>]
let ``server-side error response does not include stack trace`` () = task {
    // Provoke an internal error by pointing DB to a closed port.
    let port = getFreePort ()
    let badConn = "Host=localhost;Port=65530;Database=nope;Username=u;Password=p"

    let args =
        [| sprintf "--Server:Port=%d" port
           sprintf "--Database:ConnectionString=%s" badConn
           "--Logging:LogLevel:Default=Warning" |]

    let app = createApp args
    do! app.StartAsync()

    try
        use client = newClient port
        let! resp = client.GetAsync("/lots/2024-A-001")
        let! body = resp.Content.ReadAsStringAsync()
        Assert.DoesNotContain("at SalesManagement.", body)
        Assert.DoesNotContain("StackTrace", body)
        Assert.DoesNotContain("System.InvalidOperationException", body)
        Assert.DoesNotContain("Npgsql.NpgsqlException", body)
    finally
        app.StopAsync().GetAwaiter().GetResult()
}
