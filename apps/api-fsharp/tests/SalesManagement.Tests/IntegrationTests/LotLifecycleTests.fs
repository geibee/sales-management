module SalesManagement.Tests.IntegrationTests.LotLifecycleTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private uniqueLot () =
    let r = Random()
    let year = 3000 + r.Next(0, 5000)
    let location = "T"
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
type LotLifecycleTests(fixture: AuthOnFixture) =

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot then GET retrieves it (end-to-end)``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! getResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode)
        let! body = readBody getResp
        let root = parseJson body
        Assert.Equal("manufacturing", root.GetProperty("status").GetString())
        Assert.Equal(lotId, root.GetProperty("lotNumber").GetString())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot, complete manufacturing, DB row has status manufactured``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, _ = uniqueLot ()
        let lotId = sprintf "%d-%s-%03d" year location seq
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
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
        use client = fixture.NewAuthedClient [ "viewer" ]
        let! resp = getReq client "/lots/8888-Z-888"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = readBody resp
        let root = parseJson text
        Assert.Equal("not-found", root.GetProperty("type").GetString())
        Assert.Equal(404, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``invalid state transition returns 400 problem details``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        // Lot is in 'manufacturing' state. complete-shipping requires ShippingInstructed.
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-shipping" lotId) body
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = readBody resp
        let root = parseJson text
        Assert.Equal("invalid-state-transition", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``request without token returns 401``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots/2024-A-001"
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

        let wwwAuth =
            resp.Headers.WwwAuthenticate |> Seq.map (fun h -> h.Scheme) |> List.ofSeq

        Assert.Contains("Bearer", wwwAuth)
    }
