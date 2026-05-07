module SalesManagement.Tests.IntegrationTests.ListEndpointsTests

open System.Net
open System.Text.Json
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private lotBody (year: int) (location: string) (seq: int) =
    sprintf
        """{
            "lotNumber": { "year": %d, "location": "%s", "seq": %d },
            "divisionCode": 1,
            "departmentCode": 10,
            "sectionCode": 100,
            "processCategory": 1,
            "inspectionCategory": 1,
            "manufacturingCategory": 1,
            "details": [{
                "itemCategory": "premium",
                "premiumCategory": "A",
                "productCategoryCode": "v1",
                "lengthSpecLower": 1.0,
                "thicknessSpecLower": 1.0,
                "thicknessSpecUpper": 2.0,
                "qualityGrade": "A",
                "count": 1,
                "quantity": 10.0,
                "inspectionResultCategory": "pass"
            }]
        }"""
        year
        location
        seq

[<Collection("ApiAuthOff")>]
type ListLotsTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots returns items, total, limit, offset``() = task {
        use client = fixture.NewClient()

        // Seed two lots
        let! r1 = postJson client "/lots" (lotBody 2026 "F12A" 1)
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode)
        let! r2 = postJson client "/lots" (lotBody 2026 "F12A" 2)
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode)

        let! resp = getReq client "/lots?limit=20&offset=0"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.True(root.TryGetProperty("items") |> fst)
        Assert.True(root.TryGetProperty("total") |> fst)
        Assert.True(root.TryGetProperty("limit") |> fst)
        Assert.True(root.TryGetProperty("offset") |> fst)
        Assert.Equal(20, root.GetProperty("limit").GetInt32())
        Assert.Equal(0, root.GetProperty("offset").GetInt32())
        Assert.True(root.GetProperty("total").GetInt32() >= 2)
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots?status=manufacturing returns 200``() = task {
        use client = fixture.NewClient()

        let! _ = postJson client "/lots" (lotBody 2026 "F12B" 1)
        let! resp = getReq client "/lots?status=manufacturing"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.True(root.TryGetProperty("items") |> fst)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots with limit > 200 returns 400``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots?limit=500"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /lots with negative offset returns 400``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots?offset=-1"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

[<Collection("ApiAuthOff")>]
type ListSalesCasesTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases returns items, total``() = task {
        use client = fixture.NewClient()

        let! resp = getReq client "/sales-cases?limit=10&offset=0"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body

        Assert.True(root.TryGetProperty("items") |> fst)
        Assert.True(root.TryGetProperty("total") |> fst)
        Assert.Equal(10, root.GetProperty("limit").GetInt32())
        Assert.Equal(0, root.GetProperty("offset").GetInt32())
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases?status=appraised returns 200``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/sales-cases?status=appraised"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases with limit > 200 returns 400``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/sales-cases?limit=500"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
    }

[<Collection("ApiAuthOff")>]
type ListEndpointsOpenApiSpecTests(fixture: AuthOffFixture) =

    [<Fact>]
    [<Trait("Category", "ListEndpoints")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml contains listLots and listSalesCases``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("operationId: listLots", body)
        Assert.Contains("operationId: listSalesCases", body)
    }
