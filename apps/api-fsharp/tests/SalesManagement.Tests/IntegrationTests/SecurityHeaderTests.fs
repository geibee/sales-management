module SalesManagement.Tests.IntegrationTests.SecurityHeaderTests

open System
open System.Net
open System.Net.Http
open System.Web
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private headerValue (resp: HttpResponseMessage) (name: string) : string option =
    let succ, vals = resp.Headers.TryGetValues name

    if succ then
        Some(String.Join(",", vals))
    else
        let succ2, vals2 = resp.Content.Headers.TryGetValues name

        if succ2 then Some(String.Join(",", vals2)) else None

[<Collection("ApiAuthOff")>]
type SecurityHeadersTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /health includes X-Content-Type-Options nosniff``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let v = headerValue resp "X-Content-Type-Options"
        Assert.Equal(Some "nosniff", v)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /health includes Cross-Origin-Resource-Policy same-origin``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let v = headerValue resp "Cross-Origin-Resource-Policy"
        Assert.Equal(Some "same-origin", v)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /openapi.yaml includes security headers``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        Assert.Equal(Some "nosniff", headerValue resp "X-Content-Type-Options")
        Assert.Equal(Some "same-origin", headerValue resp "Cross-Origin-Resource-Policy")
    }

[<Collection("ApiAuthOff")>]
type LotIdValidationTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with lotId=lotId returns 400``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/api/external/price-check?lotId=lotId"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with empty lotId returns 400``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/api/external/price-check?lotId="
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with newline-containing lotId returns 400``() = task {
        use client = fixture.NewClient()
        let encoded = HttpUtility.UrlEncode("foo\nbar")
        let! resp = getReq client (sprintf "/api/external/price-check?lotId=%s" encoded)
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SecurityHeader")>]
    [<Trait("Category", "Integration")>]
    member _.``price-check with valid lotId format proceeds to upstream``() = task {
        use client = fixture.NewClient()
        // Upstream is intentionally unreachable; expect 502/503 (not 400).
        let! resp = getReq client "/api/external/price-check?lotId=2026-A-001"
        Assert.NotEqual(HttpStatusCode.BadRequest, resp.StatusCode)
        let code = int resp.StatusCode
        Assert.True(code = 502 || code = 503, sprintf "expected 502 or 503, got %d" code)
    }
