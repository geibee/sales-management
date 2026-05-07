module SalesManagement.Tests.IntegrationTests.StateTransitionParamTests

open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.TheoryCases

let private check = checkStatusAndType
let private case = tcase4

// 形式は正しいが未登録の id (lots: year-location-seq, sales-cases: year-month-seq)
let private nonExistentLotId = "9999-Z-999"
let private nonExistentSalesCaseId = "9999-12-999"

let private dvBody = dateVersionBody

[<Collection("ApiAuthOff")>]
type StateTransitionParamTests(fixture: AuthOffFixture) =

    // ───────────────────────────────────────────────────────────────
    // POST /lots/{id}/complete-manufacturing — { date, version }
    // body 不正は validation-error / id 不正は validation-error / 未登録は not-found
    // ───────────────────────────────────────────────────────────────
    static member LotCompleteManufacturingCases: obj[] seq = seq {
        // 正常境界: 正規日付 + version → 未登録 lot で 404
        yield case nonExistentLotId (dvBody "date" (JString "2026-04-15") (JInt 1)) 404 "not-found"
        yield case nonExistentLotId (dvBody "date" (JString "2026-12-31") (JInt 999)) 404 "not-found"
        // version 欠落
        yield case nonExistentLotId (dvBody "date" (JString "2026-04-15") JNull) 400 "validation-error"
        // 完全に空ボディ → version 欠落で validation-error が先
        yield case nonExistentLotId "{}" 400 "validation-error"
        // version はあるが date 欠落
        yield case nonExistentLotId (versionOnlyBody (JInt 1)) 400 "validation-error"
        // date null
        yield case nonExistentLotId (dvBody "date" JNull (JInt 1)) 400 "validation-error"
        // date 不正フォーマット
        yield case nonExistentLotId (dvBody "date" (JString "not-a-date") (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "date" (JString "") (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "date" (JString "2026-13-01") (JInt 1)) 400 "validation-error"
        // body は正しいが id フォーマット不正
        yield case "not-a-lot" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
        yield case "abc-A-1" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
        yield case "2026-A-xyz" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof StateTransitionParamTests.LotCompleteManufacturingCases)>]
    member _.``POST /lots/{id}/complete-manufacturing: date+version boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /lots/{id}/instruct-shipping — { deadline, version }
    // ───────────────────────────────────────────────────────────────
    static member LotInstructShippingCases: obj[] seq = seq {
        yield case nonExistentLotId (dvBody "deadline" (JString "2026-04-15") (JInt 1)) 404 "not-found"
        yield case nonExistentLotId (dvBody "deadline" (JString "2099-12-31") (JInt 1)) 404 "not-found"
        // version 欠落
        yield case nonExistentLotId (dvBody "deadline" (JString "2026-04-15") JNull) 400 "validation-error"
        // 空ボディ
        yield case nonExistentLotId "{}" 400 "validation-error"
        // deadline 欠落
        yield case nonExistentLotId (versionOnlyBody (JInt 1)) 400 "validation-error"
        // deadline null / 不正
        yield case nonExistentLotId (dvBody "deadline" JNull (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "deadline" (JString "not-a-date") (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "deadline" (JString "") (JInt 1)) 400 "validation-error"
        // id フォーマット不正
        yield case "not-a-lot" (dvBody "deadline" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
        yield case "abc-A-1" (dvBody "deadline" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof StateTransitionParamTests.LotInstructShippingCases)>]
    member _.``POST /lots/{id}/instruct-shipping: deadline+version boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/lots/%s/instruct-shipping" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /lots/{id}/complete-shipping — { date, version }
    // ───────────────────────────────────────────────────────────────
    static member LotCompleteShippingCases: obj[] seq = seq {
        yield case nonExistentLotId (dvBody "date" (JString "2026-04-15") (JInt 1)) 404 "not-found"
        yield case nonExistentLotId (dvBody "date" (JString "2026-04-15") JNull) 400 "validation-error"
        yield case nonExistentLotId "{}" 400 "validation-error"
        yield case nonExistentLotId (versionOnlyBody (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "date" JNull (JInt 1)) 400 "validation-error"
        yield case nonExistentLotId (dvBody "date" (JString "not-a-date") (JInt 1)) 400 "validation-error"
        yield case "not-a-lot" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "validation-error"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof StateTransitionParamTests.LotCompleteShippingCases)>]
    member _.``POST /lots/{id}/complete-shipping: date+version boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/lots/%s/complete-shipping" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/shipping-instruction — { date, version }
    // body / id 不正は bad-request、未登録は not-found
    // ───────────────────────────────────────────────────────────────
    static member SalesCaseShippingInstructionCases: obj[] seq = seq {
        // 正常境界 → 未登録 sales case で 404
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-04-15") (JInt 1)) 404 "not-found"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-12-31") (JInt 999)) 404 "not-found"
        // version 欠落
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-04-15") JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        // date 欠落・null・不正
        yield case nonExistentSalesCaseId (versionOnlyBody (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" JNull (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "not-a-date") (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "") (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-13-01") (JInt 1)) 400 "bad-request"
        // sales case id フォーマット不正 → trySalesCaseNumber が先に失敗
        yield case "not-a-case" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "bad-request"
        yield case "2026-04" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "bad-request"
        yield case "abc-04-001" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof StateTransitionParamTests.SalesCaseShippingInstructionCases)>]
    member _.``POST /sales-cases/{id}/shipping-instruction: date+version boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/shipping-instruction" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/shipping-completion — { date, version }
    // ───────────────────────────────────────────────────────────────
    static member SalesCaseShippingCompletionCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-04-15") (JInt 1)) 404 "not-found"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "2026-04-15") JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId (versionOnlyBody (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" JNull (JInt 1)) 400 "bad-request"
        yield case nonExistentSalesCaseId (dvBody "date" (JString "not-a-date") (JInt 1)) 400 "bad-request"
        yield case "not-a-case" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "bad-request"
        yield case "abc-04-001" (dvBody "date" (JString "2026-04-15") (JInt 1)) 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof StateTransitionParamTests.SalesCaseShippingCompletionCases)>]
    member _.``POST /sales-cases/{id}/shipping-completion: date+version boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/shipping-completion" id) body
            do! check status expectedType resp
        }
