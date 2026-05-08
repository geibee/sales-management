module SalesManagement.Tests.IntegrationTests.LotRoutesParamTests

open System.Net.Http
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.TheoryCases

let private bodyWithYear (year: JsonValue) : string =
    createLotBody
        { emptyLotOverrides with
            Year = Some year }

let private bodyWithLocation (loc: JsonValue) : string =
    createLotBody
        { emptyLotOverrides with
            Location = Some loc }

let private bodyWithSeq (s: JsonValue) : string =
    createLotBody { emptyLotOverrides with Seq = Some s }

let private bodyWithDetails (details: JsonValue) : string =
    createLotBody
        { emptyLotOverrides with
            Details = Some details }

let private case a b c = tcase3 a b c
let private detailWith = lotDetailWith
let private check = checkStatusAndType

[<Collection("ApiAuthOff")>]
type LotRoutesParamTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    // ───────────────────────────────────────────────────────────────
    // POST /lots — lotNumber.year 境界 (PositiveInt: > 0)
    // ───────────────────────────────────────────────────────────────
    static member YearCases: obj[] seq = seq {
        yield case (bodyWithYear (JInt 1)) 200 ""
        yield case (bodyWithYear (JInt 2026)) 200 ""
        yield case (bodyWithYear (JInt 9999)) 200 ""
        yield case (bodyWithYear (JInt 0)) 400 "validation-error"
        yield case (bodyWithYear (JInt -1)) 400 "validation-error"
        yield case (bodyWithYear JNull) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.YearCases)>]
    member _.``POST /lots: lotNumber.year boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — lotNumber.location 境界 (NonEmptyString)
    // ───────────────────────────────────────────────────────────────
    static member LocationCases: obj[] seq = seq {
        yield case (bodyWithLocation (JString "F12A")) 200 ""
        yield case (bodyWithLocation (JString "A")) 200 ""
        yield case (bodyWithLocation (JString "")) 400 "validation-error"
        yield case (bodyWithLocation (JString "   ")) 400 "validation-error"
        yield case (bodyWithLocation JNull) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.LocationCases)>]
    member _.``POST /lots: lotNumber.location boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — lotNumber.seq 境界 (PositiveInt: > 0)
    // ───────────────────────────────────────────────────────────────
    static member SeqCases: obj[] seq = seq {
        yield case (bodyWithSeq (JInt 1)) 200 ""
        yield case (bodyWithSeq (JInt 999)) 200 ""
        yield case (bodyWithSeq (JInt 0)) 400 "validation-error"
        yield case (bodyWithSeq (JInt -1)) 400 "validation-error"
        yield case (bodyWithSeq JNull) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.SeqCases)>]
    member _.``POST /lots: lotNumber.seq boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — details[].count 境界 (Count: >= 1)
    // ───────────────────────────────────────────────────────────────
    static member DetailsCountCases: obj[] seq = seq {
        yield case (bodyWithDetails (JArray [ detailWith [ "count", JInt 1 ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "count", JInt 100 ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "count", JInt 0 ] ])) 400 "validation-error"
        yield case (bodyWithDetails (JArray [ detailWith [ "count", JInt -1 ] ])) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.DetailsCountCases)>]
    member _.``POST /lots: details[].count boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — details[].quantity 境界 (Quantity: >= 0.001)
    // ───────────────────────────────────────────────────────────────
    static member DetailsQuantityCases: obj[] seq = seq {
        yield case (bodyWithDetails (JArray [ detailWith [ "quantity", JDecimal 0.001m ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "quantity", JDecimal 1m ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "quantity", JDecimal 0m ] ])) 400 "validation-error"
        yield case (bodyWithDetails (JArray [ detailWith [ "quantity", JDecimal 0.0009m ] ])) 400 "validation-error"
        yield case (bodyWithDetails (JArray [ detailWith [ "quantity", JDecimal -1m ] ])) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.DetailsQuantityCases)>]
    member _.``POST /lots: details[].quantity boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — details[].itemCategory enum
    // ───────────────────────────────────────────────────────────────
    static member DetailsItemCategoryCases: obj[] seq = seq {
        yield case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "general" ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "premium" ] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "custom" ] ])) 200 ""

        yield
            case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "unknown" ] ])) 400 "validation-error"

        yield case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "" ] ])) 400 "validation-error"

        yield
            case (bodyWithDetails (JArray [ detailWith [ "itemCategory", JString "PREMIUM" ] ])) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.DetailsItemCategoryCases)>]
    member _.``POST /lots: details[].itemCategory enum``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // POST /lots — details 配列長 (NonEmptyList: >= 1)
    // ───────────────────────────────────────────────────────────────
    static member DetailsArrayCases: obj[] seq = seq {
        yield case (bodyWithDetails (JArray [])) 400 "validation-error"
        yield case (bodyWithDetails (JArray [ detailWith [] ])) 200 ""
        yield case (bodyWithDetails (JArray [ detailWith []; detailWith [] ])) 200 ""
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.DetailsArrayCases)>]
    member _.``POST /lots: details array length boundaries``(body: string, status: int, expectedType: string) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! resp = postJson client "/lots" body
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots/{id} — id フォーマット境界 (year-location-seq)
    // ───────────────────────────────────────────────────────────────
    static member GetLotIdCases: obj[] seq = seq {
        // 形式は正しいが未登録 → 404 not-found
        yield case "9999-Z-999" 404 "not-found"
        yield case "1-A-1" 404 "not-found"
        // 形式不正 → 400 validation-error
        yield case "not-a-lot" 400 "validation-error"
        yield case "2026-A" 400 "validation-error"
        yield case "2026--1" 400 "validation-error"
        yield case "abc-A-1" 400 "validation-error"
        yield case "2026-A-xyz" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.GetLotIdCases)>]
    member _.``GET /lots/{id}: id format boundaries``(id: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client (sprintf "/lots/%s" id)
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots — limit 境界 (1 <= limit <= 200)
    // ───────────────────────────────────────────────────────────────
    static member ListLimitCases: obj[] seq = seq {
        yield case "/lots" 200 ""
        yield case "/lots?limit=1" 200 ""
        yield case "/lots?limit=200" 200 ""
        yield case "/lots?limit=50" 200 ""
        yield case "/lots?limit=0" 400 "validation-error"
        yield case "/lots?limit=201" 400 "validation-error"
        yield case "/lots?limit=-1" 400 "validation-error"
        yield case "/lots?limit=abc" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.ListLimitCases)>]
    member _.``GET /lots: limit boundaries``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots — offset 境界 (offset >= 0)
    // ───────────────────────────────────────────────────────────────
    static member ListOffsetCases: obj[] seq = seq {
        yield case "/lots?offset=0" 200 ""
        yield case "/lots?offset=10" 200 ""
        yield case "/lots?offset=-1" 400 "validation-error"
        yield case "/lots?offset=abc" 400 "validation-error"
        yield case "/lots?limit=10&offset=0" 200 ""
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof LotRoutesParamTests.ListOffsetCases)>]
    member _.``GET /lots: offset boundaries``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }
