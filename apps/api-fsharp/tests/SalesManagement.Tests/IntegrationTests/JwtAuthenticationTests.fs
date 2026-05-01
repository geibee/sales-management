module SalesManagement.Tests.IntegrationTests.JwtAuthenticationTests

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
open Xunit
open SalesManagement.Hosting

let private signingKey =
    // 256-bit symmetric key for HS256.
    "step03-test-signing-key-please-do-not-use-in-production"

let private audience = "sales-api"

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

let private startAuthApp () : WebApplication * int =
    let port = getFreePort ()

    let args =
        [| sprintf "--Server:Port=%d" port
           sprintf "--Database:ConnectionString=%s" connectionString
           "--Authentication:Enabled=true"
           sprintf "--Authentication:SigningKey=%s" signingKey
           sprintf "--Authentication:Audience=%s" audience
           "--Logging:LogLevel:Default=Warning" |]

    let app = createApp args
    app.StartAsync().GetAwaiter().GetResult()
    app, port

let private stopApp (app: WebApplication) =
    app.StopAsync().GetAwaiter().GetResult()

let private newClient (port: int) =
    let client = new HttpClient()
    client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" port)
    client

let private mintToken (roles: string list) (expiresInSec: float) : string =
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
            issuer = "step03-test",
            audience = audience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow.AddSeconds expiresInSec),
            signingCredentials = creds
        )

    JwtSecurityTokenHandler().WriteToken token

let private postJson
    (client: HttpClient)
    (path: string)
    (body: string)
    (token: string option)
    : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Post, path)
    req.Content <- new StringContent(body, Encoding.UTF8, "application/json")

    match token with
    | Some t -> req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
    | None -> ()

    client.SendAsync req

let private getWithToken (client: HttpClient) (path: string) (token: string option) : Task<HttpResponseMessage> =
    let req = new HttpRequestMessage(HttpMethod.Get, path)

    match token with
    | Some t -> req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
    | None -> ()

    client.SendAsync req

let private uniqueLot () : int * string * int * string =
    let r = Random()
    let year = 2060 + r.Next(0, 5000)
    let location = "Z"
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

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``GET /health is reachable without a token`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let! resp = getWithToken client "/health" None
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``GET /lots/{id} without token returns 401 with WWW-Authenticate Bearer`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let! resp = getWithToken client "/lots/2024-A-001" None
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

        let wwwAuth =
            resp.Headers.WwwAuthenticate |> Seq.map (fun h -> h.Scheme) |> List.ofSeq

        Assert.Contains("Bearer", wwwAuth)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``GET /lots/{id} with expired token returns 401`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let token = mintToken [ "viewer" ] -60.0
        let! resp = getWithToken client "/lots/2024-A-001" (Some token)
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``viewer can GET lot but cannot POST state transition`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let operatorToken = mintToken [ "operator" ] 300.0
        let viewerToken = mintToken [ "viewer" ] 300.0

        // operator creates a lot
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some operatorToken)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        // viewer can read it
        let! readResp = getWithToken client (sprintf "/lots/%s" lotId) (Some viewerToken)
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode)

        // viewer attempting state transition is forbidden
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! mutateResp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body (Some viewerToken)
        Assert.Equal(HttpStatusCode.Forbidden, mutateResp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``operator can perform state transitions`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let operatorToken = mintToken [ "operator" ] 300.0
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some operatorToken)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! mutateResp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body (Some operatorToken)
        Assert.Equal(HttpStatusCode.OK, mutateResp.StatusCode)
        let! text = mutateResp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse text
        Assert.Equal("manufactured", doc.RootElement.GetProperty("status").GetString())
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``admin inherits operator permissions`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let adminToken = mintToken [ "admin" ] 300.0
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some adminToken)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! readResp = getWithToken client (sprintf "/lots/%s" lotId) (Some adminToken)
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode)
    finally
        stopApp app
}

[<Fact>]
[<Trait("Category", "JwtAuthentication")>]
let ``token without any acceptable role is forbidden on mutations`` () = task {
    let app, port = startAuthApp ()

    try
        use client = newClient port
        let unknownToken = mintToken [ "guest" ] 300.0
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client "/lots/2024-A-001/complete-manufacturing" body (Some unknownToken)
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode)
    finally
        stopApp app
}
