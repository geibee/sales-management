module SalesManagement.Tests.Support.SmokeTest

open System.Net
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.RequestBuilders

[<Collection("ApiAuthOff")>]
type SupportSmokeTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "Smoke")>]
    member _.``smoke: GET /health returns UP via Support helpers``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/health"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("UP", root.GetProperty("status").GetString())
    }

    [<Fact>]
    [<Trait("Category", "Smoke")>]
    member _.``smoke: POST /lots with default body returns 200``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let body = createLotBody emptyLotOverrides
        let! resp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "Smoke")>]
    member _.``smoke: GET /lots?limit=500 returns ProblemDetails``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots?limit=500"
        let! _ = assertProblemDetails "" HttpStatusCode.BadRequest resp
        ()
    }
