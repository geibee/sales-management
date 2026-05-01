module SalesManagement.Tests.IntegrationTests.LotLifecycleTests

open System
open System.IO
open System.IdentityModel.Tokens.Jwt
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.IdentityModel.Tokens
open Npgsql
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting

let private signingKey = "step05-test-signing-key-please-do-not-use-in-production"

let private audience = "sales-api"

let private migrationsDir =
    // base dir = tests/SalesManagement.Tests/bin/Debug/net10.0 → up 5 → fsharp/, then migrations
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

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
            issuer = "step05-test",
            audience = audience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow.AddSeconds 300.0),
            signingCredentials = creds
        )

    JwtSecurityTokenHandler().WriteToken token

type LotLifecycleFixture() =
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
                       "--Authentication:Enabled=true"
                       sprintf "--Authentication:SigningKey=%s" signingKey
                       sprintf "--Authentication:Audience=%s" audience
                       "--Logging:LogLevel:Default=Warning" |]

                app <- createApp args
                do! app.StartAsync()
            }
            :> Task

        member _.DisposeAsync() : Task =
            task {
                if not (isNull (box app)) then
                    do! app.StopAsync()

                if not (isNull (box container)) then
                    do! container.DisposeAsync()
            }
            :> Task

[<CollectionDefinition("LotLifecycle")>]
type LotLifecycleCollection() =
    interface ICollectionFixture<LotLifecycleFixture>

[<Collection("LotLifecycle")>]
type LotLifecycleTests(fixture: LotLifecycleFixture) =
    let newClient () =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" fixture.Port)
        client

    let postJson (client: HttpClient) (path: string) (body: string) (token: string option) : Task<HttpResponseMessage> =
        let req = new HttpRequestMessage(HttpMethod.Post, path)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")

        match token with
        | Some t -> req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
        | None -> ()

        client.SendAsync req

    let getWith (client: HttpClient) (path: string) (token: string option) : Task<HttpResponseMessage> =
        let req = new HttpRequestMessage(HttpMethod.Get, path)

        match token with
        | Some t -> req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", t)
        | None -> ()

        client.SendAsync req

    let uniqueLot () =
        let r = Random()
        let year = 3000 + r.Next(0, 5000)
        let location = "T"
        let seq = r.Next(1, 999)
        let id = sprintf "%d-%s-%03d" year location seq
        year, location, seq, id

    let createLotBody (year: int) (location: string) (seq: int) =
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
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot then GET retrieves it (end-to-end)``() = task {
        use client = newClient ()
        let token = mintToken [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some token)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! getResp = getWith client (sprintf "/lots/%s" lotId) (Some token)
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode)
        let! body = getResp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse body
        let root = doc.RootElement
        Assert.Equal("manufacturing", root.GetProperty("status").GetString())
        Assert.Equal(lotId, root.GetProperty("lotNumber").GetString())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot, complete manufacturing, DB row has status manufactured``() = task {
        use client = newClient ()
        let token = mintToken [ "operator" ]
        let year, location, seq, _ = uniqueLot ()
        let lotId = sprintf "%d-%s-%03d" year location seq
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some token)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body (Some token)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT status FROM lot WHERE lot_number_year = @y AND lot_number_location = @l AND lot_number_seq = @s"

        let py = cmd.CreateParameter()
        py.ParameterName <- "y"
        py.Value <- year
        cmd.Parameters.Add py |> ignore
        let pl = cmd.CreateParameter()
        pl.ParameterName <- "l"
        pl.Value <- location
        cmd.Parameters.Add pl |> ignore
        let ps = cmd.CreateParameter()
        ps.ParameterName <- "s"
        ps.Value <- seq
        cmd.Parameters.Add ps |> ignore

        let result = cmd.ExecuteScalar()
        Assert.NotNull(result)
        Assert.Equal("manufactured", result.ToString())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``GET non-existent lot returns 404 problem details``() = task {
        use client = newClient ()
        let token = mintToken [ "viewer" ]
        let! resp = getWith client "/lots/8888-Z-888" (Some token)
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse text
        let root = doc.RootElement
        Assert.Equal("not-found", root.GetProperty("type").GetString())
        Assert.Equal(404, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``invalid state transition returns 400 problem details``() = task {
        use client = newClient ()
        let token = mintToken [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (createLotBody year location seq) (Some token)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        // Lot is in 'manufacturing' state. complete-shipping requires ShippingInstructed.
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-shipping" lotId) body (Some token)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = resp.Content.ReadAsStringAsync()
        use doc = JsonDocument.Parse text
        let root = doc.RootElement
        Assert.Equal("invalid-state-transition", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``request without token returns 401``() = task {
        use client = newClient ()
        let! resp = getWith client "/lots/2024-A-001" None
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

        let wwwAuth =
            resp.Headers.WwwAuthenticate |> Seq.map (fun h -> h.Scheme) |> List.ofSeq

        Assert.Contains("Bearer", wwwAuth)
    }
