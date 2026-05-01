module SalesManagement.Tests.IntegrationTests.AuditAndOtelTests

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IO
open System.IdentityModel.Tokens.Jwt
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Sockets
open System.Security.Claims
open System.Text
open System.Threading.Tasks
open DbUp
open Microsoft.AspNetCore.Builder
open Microsoft.IdentityModel.Tokens
open Npgsql
open Testcontainers.PostgreSql
open Xunit
open SalesManagement.Hosting

let private signingKey = "step08-test-signing-key-please-do-not-use-in-production"
let private audience = "sales-api"

let private migrationsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "migrations"))

let private getFreePort () =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private mintTokenWithSub (sub: string) (roles: string list) : string =
    let keyBytes = Encoding.UTF8.GetBytes signingKey
    let key = SymmetricSecurityKey keyBytes
    let creds = SigningCredentials(key, SecurityAlgorithms.HmacSha256)

    let realmAccess =
        let payload =
            sprintf """{"roles":[%s]}""" (roles |> List.map (sprintf "\"%s\"") |> String.concat ",")

        Claim("realm_access", payload, JsonClaimValueTypes.Json)

    let claims =
        [| Claim("sub", sub)
           Claim("preferred_username", roles |> List.tryHead |> Option.defaultValue "anonymous")
           realmAccess |]

    let token =
        JwtSecurityToken(
            issuer = "step08-test",
            audience = audience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow.AddSeconds 300.0),
            signingCredentials = creds
        )

    JwtSecurityTokenHandler().WriteToken token

type AuditAndOtelFixture() =
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
                       "--Outbox:PollIntervalMs=500"
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

[<CollectionDefinition("AuditAndOtel")>]
type AuditAndOtelCollection() =
    interface ICollectionFixture<AuditAndOtelFixture>

[<Collection("AuditAndOtel")>]
type AuditAndOtelTests(fixture: AuditAndOtelFixture) =
    let newClient () =
        let client = new HttpClient()
        client.BaseAddress <- Uri(sprintf "http://127.0.0.1:%d" fixture.Port)
        client

    let postJson (client: HttpClient) (path: string) (body: string) (token: string) : Task<HttpResponseMessage> =
        let req = new HttpRequestMessage(HttpMethod.Post, path)
        req.Content <- new StringContent(body, Encoding.UTF8, "application/json")
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        client.SendAsync req

    let getReq (client: HttpClient) (path: string) (token: string) : Task<HttpResponseMessage> =
        let req = new HttpRequestMessage(HttpMethod.Get, path)
        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
        client.SendAsync req

    let uniqueLot () =
        let r = Random()
        let year = 5000 + r.Next(0, 4000)
        let location = "F"
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

    let queryAuditColumns (year: int) (location: string) (seq: int) : (string * string * DateTime * DateTime) option =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT created_by, updated_by, created_at, updated_at FROM lot WHERE lot_number_year = @y AND lot_number_location = @l AND lot_number_seq = @s"

        let p1 = cmd.CreateParameter()
        p1.ParameterName <- "y"
        p1.Value <- year
        cmd.Parameters.Add p1 |> ignore
        let p2 = cmd.CreateParameter()
        p2.ParameterName <- "l"
        p2.Value <- location
        cmd.Parameters.Add p2 |> ignore
        let p3 = cmd.CreateParameter()
        p3.ParameterName <- "s"
        p3.Value <- seq
        cmd.Parameters.Add p3 |> ignore
        use reader = cmd.ExecuteReader()

        if reader.Read() then
            Some(reader.GetString 0, reader.GetString 1, reader.GetDateTime 2, reader.GetDateTime 3)
        else
            None

    [<Fact>]
    [<Trait("Category", "AuditAndOtel")>]
    [<Trait("Category", "Integration")>]
    member _.``creating a lot records created_by from JWT sub claim``() = task {
        use client = newClient ()
        let operatorSub = Guid.NewGuid().ToString()
        let token = mintTokenWithSub operatorSub [ "operator" ]
        let year, location, seq, _ = uniqueLot ()

        let! createResp = postJson client "/lots" (createLotBody year location seq) token
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        match queryAuditColumns year location seq with
        | None -> Assert.Fail "lot row not found"
        | Some(createdBy, updatedBy, _, _) ->
            Assert.Equal(operatorSub, createdBy)
            Assert.Equal(operatorSub, updatedBy)
    }

    [<Fact>]
    [<Trait("Category", "AuditAndOtel")>]
    [<Trait("Category", "Integration")>]
    member _.``updating a lot keeps created_by but changes updated_by``() = task {
        use client = newClient ()
        let operatorSub = Guid.NewGuid().ToString()
        let adminSub = Guid.NewGuid().ToString()
        let opToken = mintTokenWithSub operatorSub [ "operator" ]
        let adminToken = mintTokenWithSub adminSub [ "admin" ]
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (createLotBody year location seq) opToken
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""

        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body adminToken

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        match queryAuditColumns year location seq with
        | None -> Assert.Fail "lot row not found after update"
        | Some(createdBy, updatedBy, createdAt, updatedAt) ->
            Assert.Equal(operatorSub, createdBy)
            Assert.Equal(adminSub, updatedBy)
            Assert.True(updatedAt >= createdAt, "updated_at should be at or after created_at")
    }

    [<Fact>]
    [<Trait("Category", "AuditAndOtel")>]
    [<Trait("Category", "Integration")>]
    member _.``HTTP requests produce OpenTelemetry activities``() = task {
        let captured = ConcurrentBag<Activity>()

        let listener =
            new ActivityListener(
                ShouldListenTo = (fun src -> src.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)),
                Sample = (fun (_: byref<ActivityCreationOptions<ActivityContext>>) -> ActivitySamplingResult.AllData),
                ActivityStopped = (fun a -> captured.Add a)
            )

        ActivitySource.AddActivityListener listener

        try
            use client = newClient ()
            let token = mintTokenWithSub (Guid.NewGuid().ToString()) [ "operator" ]
            let year, location, seq, _ = uniqueLot ()

            let! createResp = postJson client "/lots" (createLotBody year location seq) token
            Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

            do! Task.Delay 200

            Assert.NotEmpty(captured)

            let httpActivities =
                captured
                |> Seq.filter (fun a -> a.OperationName.Contains("HttpRequestIn", StringComparison.Ordinal))
                |> Seq.toList

            Assert.NotEmpty httpActivities

            for a in httpActivities do
                Assert.NotEqual(Unchecked.defaultof<ActivityTraceId>, a.TraceId)
                Assert.NotEqual(Unchecked.defaultof<ActivitySpanId>, a.SpanId)
        finally
            listener.Dispose()
    }
