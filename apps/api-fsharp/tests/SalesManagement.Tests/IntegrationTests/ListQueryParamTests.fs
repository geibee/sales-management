module SalesManagement.Tests.IntegrationTests.ListQueryParamTests

open System.Net.Http
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.TheoryCases

let private case a b c = tcase3 a b c
let private check = checkStatusAndType

[<Collection("ApiAuthOff")>]
type ListQueryParamTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    // ───────────────────────────────────────────────────────────────
    // GET /lots — limit 境界 (1 <= limit <= 200, default 50)
    // 不正は ValidationFailed → "validation-error"
    // ───────────────────────────────────────────────────────────────
    static member LotsLimitCases: obj[] seq = seq {
        // 正常境界
        yield case "/lots?limit=1" 200 ""
        yield case "/lots?limit=50" 200 ""
        yield case "/lots?limit=200" 200 ""
        // 空文字 → 既定値 50 にフォールバック
        yield case "/lots?limit=" 200 ""
        // 範囲外
        yield case "/lots?limit=0" 400 "validation-error"
        yield case "/lots?limit=201" 400 "validation-error"
        yield case "/lots?limit=-1" 400 "validation-error"
        // 型違反
        yield case "/lots?limit=abc" 400 "validation-error"
        yield case "/lots?limit=1.5" 400 "validation-error"
        yield case "/lots?limit=99999999999999999" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.LotsLimitCases)>]
    member _.``GET /lots: limit matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots — offset 境界 (offset >= 0, default 0)
    // ───────────────────────────────────────────────────────────────
    static member LotsOffsetCases: obj[] seq = seq {
        yield case "/lots?offset=0" 200 ""
        yield case "/lots?offset=10" 200 ""
        yield case "/lots?offset=99999" 200 ""
        // 空文字 → 既定値
        yield case "/lots?offset=" 200 ""
        // 範囲外
        yield case "/lots?offset=-1" 400 "validation-error"
        // 型違反
        yield case "/lots?offset=abc" 400 "validation-error"
        yield case "/lots?offset=1.5" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.LotsOffsetCases)>]
    member _.``GET /lots: offset matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots — limit/offset/status 組合せ
    // status は free-form でリポジトリ側に渡るだけ → 任意文字列で 200
    // ───────────────────────────────────────────────────────────────
    static member LotsComboCases: obj[] seq = seq {
        yield case "/lots" 200 ""
        yield case "/lots?limit=10&offset=0" 200 ""
        yield case "/lots?limit=10&offset=0&status=manufacturing" 200 ""
        yield case "/lots?limit=200&offset=0&status=completed" 200 ""
        yield case "/lots?status=manufacturing" 200 ""
        yield case "/lots?status=anything-goes" 200 ""
        yield case "/lots?status=" 200 ""
        // 一方のパラメータが不正なら全体が 400
        yield case "/lots?limit=300&offset=0" 400 "validation-error"
        yield case "/lots?limit=10&offset=-5" 400 "validation-error"
        yield case "/lots?limit=abc&offset=0" 400 "validation-error"
        yield case "/lots?limit=10&offset=xyz" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.LotsComboCases)>]
    member _.``GET /lots: combination matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /lots — 未知キーは無視 / 重複指定はパース失敗で 400
    // ASP.NET の StringValues は同名キーをカンマ結合するため
    // "?limit=10&limit=20" は "10,20" として Int32.TryParse 失敗 → 400
    // ───────────────────────────────────────────────────────────────
    static member LotsKeyHandlingCases: obj[] seq = seq {
        // 未知キーは無視され 200 を返す
        yield case "/lots?sort=createdAt" 200 ""
        yield case "/lots?filter=foo" 200 ""
        yield case "/lots?unknown=bar" 200 ""
        yield case "/lots?sort=createdAt&filter=foo&unknown=bar" 200 ""
        yield case "/lots?limit=10&sort=desc&offset=0" 200 ""
        // 重複キーはカンマ結合により int パース失敗 → 400
        yield case "/lots?limit=10&limit=20" 400 "validation-error"
        yield case "/lots?offset=0&offset=10" 400 "validation-error"
        yield case "/lots?limit=10&limit=10" 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.LotsKeyHandlingCases)>]
    member _.``GET /lots: unknown/duplicate key matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /sales-cases — limit 境界 (1 <= limit <= 200, default 50)
    // listSalesCasesHandler は badRequest を返すので type は "bad-request"
    // ───────────────────────────────────────────────────────────────
    static member SalesCasesLimitCases: obj[] seq = seq {
        yield case "/sales-cases?limit=1" 200 ""
        yield case "/sales-cases?limit=50" 200 ""
        yield case "/sales-cases?limit=200" 200 ""
        yield case "/sales-cases?limit=" 200 ""
        yield case "/sales-cases?limit=0" 400 "bad-request"
        yield case "/sales-cases?limit=201" 400 "bad-request"
        yield case "/sales-cases?limit=-1" 400 "bad-request"
        yield case "/sales-cases?limit=abc" 400 "bad-request"
        yield case "/sales-cases?limit=1.5" 400 "bad-request"
        yield case "/sales-cases?limit=99999999999999999" 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.SalesCasesLimitCases)>]
    member _.``GET /sales-cases: limit matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /sales-cases — offset 境界
    // ───────────────────────────────────────────────────────────────
    static member SalesCasesOffsetCases: obj[] seq = seq {
        yield case "/sales-cases?offset=0" 200 ""
        yield case "/sales-cases?offset=10" 200 ""
        yield case "/sales-cases?offset=99999" 200 ""
        yield case "/sales-cases?offset=" 200 ""
        yield case "/sales-cases?offset=-1" 400 "bad-request"
        yield case "/sales-cases?offset=abc" 400 "bad-request"
        yield case "/sales-cases?offset=1.5" 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.SalesCasesOffsetCases)>]
    member _.``GET /sales-cases: offset matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /sales-cases — limit/offset/status/caseType 組合せ
    // status, caseType は free-form (リポジトリ側で SQL filter)
    // ───────────────────────────────────────────────────────────────
    static member SalesCasesComboCases: obj[] seq = seq {
        yield case "/sales-cases" 200 ""
        yield case "/sales-cases?limit=10&offset=0" 200 ""
        yield case "/sales-cases?limit=10&offset=0&status=appraised" 200 ""
        yield case "/sales-cases?caseType=direct" 200 ""
        yield case "/sales-cases?caseType=reservation" 200 ""
        yield case "/sales-cases?caseType=consignment" 200 ""
        yield case "/sales-cases?caseType=anything-goes" 200 ""
        yield case "/sales-cases?status=anything&caseType=anything" 200 ""
        yield case "/sales-cases?status=" 200 ""
        yield case "/sales-cases?caseType=" 200 ""
        // 不正パラメータ
        yield case "/sales-cases?limit=300&offset=0" 400 "bad-request"
        yield case "/sales-cases?limit=10&offset=-5" 400 "bad-request"
        yield case "/sales-cases?limit=abc&offset=0" 400 "bad-request"
        yield case "/sales-cases?limit=10&offset=xyz" 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.SalesCasesComboCases)>]
    member _.``GET /sales-cases: combination matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }

    // ───────────────────────────────────────────────────────────────
    // GET /sales-cases — 未知キーは無視 / 重複指定は 400
    // ───────────────────────────────────────────────────────────────
    static member SalesCasesKeyHandlingCases: obj[] seq = seq {
        yield case "/sales-cases?sort=createdAt" 200 ""
        yield case "/sales-cases?filter=foo" 200 ""
        yield case "/sales-cases?unknown=bar" 200 ""
        yield case "/sales-cases?sort=createdAt&filter=foo&unknown=bar" 200 ""
        yield case "/sales-cases?limit=10&sort=desc&offset=0" 200 ""
        yield case "/sales-cases?limit=10&limit=20" 400 "bad-request"
        yield case "/sales-cases?offset=0&offset=10" 400 "bad-request"
        yield case "/sales-cases?limit=10&limit=10" 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ListQueryParamTests.SalesCasesKeyHandlingCases)>]
    member _.``GET /sales-cases: unknown/duplicate key matrix``(path: string, status: int, expectedType: string) = task {
        use client = fixture.NewClient()
        let! resp = getReq client path
        do! check status expectedType resp
    }
