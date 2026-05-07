module SalesManagement.Tests.IntegrationTests.AuditAndOtelTests

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.IdentityModel.Tokens.Jwt
open System.Net
open System.Net.Http.Headers
open System.Security.Claims
open System.Text
open System.Threading.Tasks
open Microsoft.IdentityModel.Tokens
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private mintTokenWithSub (sub: string) (roles: string list) : string =
    let keyBytes = Encoding.UTF8.GetBytes testSigningKey
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
            issuer = "support-fixture",
            audience = testAudience,
            claims = claims,
            expires = Nullable(DateTime.UtcNow.AddSeconds 300.0),
            signingCredentials = creds
        )

    JwtSecurityTokenHandler().WriteToken token

let private uniqueLot () =
    let r = Random()
    let year = 5000 + r.Next(0, 4000)
    let location = "F"
    let seq = r.Next(1, 999)
    let id = sprintf "%d-%s-%03d" year location seq
    year, location, seq, id

let private lotBody (year: int) (location: string) (seq: int) =
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

[<Collection("ApiAuthOn")>]
type AuditAndOtelTests(fixture: AuthOnFixture) =
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
        use client = fixture.NewClient()
        let operatorSub = Guid.NewGuid().ToString()
        let token = mintTokenWithSub operatorSub [ "operator" ]
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let year, location, seq, _ = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
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
        use opClient = fixture.NewClient()
        use adminClient = fixture.NewClient()
        let operatorSub = Guid.NewGuid().ToString()
        let adminSub = Guid.NewGuid().ToString()
        let opToken = mintTokenWithSub operatorSub [ "operator" ]
        let adminToken = mintTokenWithSub adminSub [ "admin" ]
        opClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", opToken)
        adminClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", adminToken)
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson opClient "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson adminClient (sprintf "/lots/%s/complete-manufacturing" lotId) body

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
            use client = fixture.NewClient()
            let token = mintTokenWithSub (Guid.NewGuid().ToString()) [ "operator" ]
            client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
            let year, location, seq, _ = uniqueLot ()

            let! createResp = postJson client "/lots" (lotBody year location seq)
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
