module SalesManagement.Tests.IntegrationTests.JwtAuthenticationTests

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Text.Json
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private uniqueLot () : int * string * int * string =
    let r = Random()
    let year = 2060 + r.Next(0, 5000)
    let location = "Z"
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
type JwtAuthenticationTests(fixture: AuthOnFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /health is reachable without a token``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots/{id} without token returns 401 with WWW-Authenticate Bearer``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots/2024-A-001"
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

        let wwwAuth =
            resp.Headers.WwwAuthenticate |> Seq.map (fun h -> h.Scheme) |> List.ofSeq

        Assert.Contains("Bearer", wwwAuth)
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots/{id} with expired token returns 401``() = task {
        use client = fixture.NewClient()
        let token = ApiFixture.MintToken([ "viewer" ], -60.0)
        client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
        let! resp = getReq client "/lots/2024-A-001"
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``viewer can GET lot but cannot POST state transition``() = task {
        use operatorClient = fixture.NewAuthedClient [ "operator" ]
        use viewerClient = fixture.NewAuthedClient [ "viewer" ]

        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson operatorClient "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! readResp = getReq viewerClient (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! mutateResp = postJson viewerClient (sprintf "/lots/%s/complete-manufacturing" lotId) body
        Assert.Equal(HttpStatusCode.Forbidden, mutateResp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``operator can perform state transitions``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! mutateResp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
        Assert.Equal(HttpStatusCode.OK, mutateResp.StatusCode)
        let! text = readBody mutateResp
        let root = parseJson text
        Assert.Equal("manufactured", root.GetProperty("status").GetString())
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``admin inherits operator permissions``() = task {
        use client = fixture.NewAuthedClient [ "admin" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! readResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, readResp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "JwtAuthentication")>]
    [<Trait("Category", "Integration")>]
    member _.``token without any acceptable role is forbidden on mutations``() = task {
        use client = fixture.NewAuthedClient [ "guest" ]
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client "/lots/2024-A-001/complete-manufacturing" body
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode)
    }
