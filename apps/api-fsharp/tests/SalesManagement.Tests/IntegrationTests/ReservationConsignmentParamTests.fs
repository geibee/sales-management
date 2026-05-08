module SalesManagement.Tests.IntegrationTests.ReservationConsignmentParamTests

open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.TheoryCases

let private check = checkStatusAndType
let private case = tcase4

// 形式は正しいが未登録の sales case id (year-month-seq)
let private nonExistentSalesCaseId = "9999-12-999"

// ───────────────────────────────────────────────────────────────
// ボディビルダ — 各エンドポイントの DTO 形に合わせて差分指定可能にする。
// reservation/consignment 系のルートは bindBody → requireVersion → trySalesCaseNumber
// → tryFindHeader の順に検証するため、version 欠落は他のフィールド不正より先に
// bad-request として現れる。
// ───────────────────────────────────────────────────────────────

let private appraisalBody
    (appraisalDate: JsonValue)
    (reservedLotInfo: JsonValue)
    (reservedAmount: JsonValue)
    (version: JsonValue)
    : string =
    render (
        JObject
            [ "appraisalDate", appraisalDate
              "reservedLotInfo", reservedLotInfo
              "reservedAmount", reservedAmount
              "version", version ]
    )

let private validAppraisalBody =
    appraisalBody (JString "2026-04-15") (JString "L-1") (JInt 1000) (JInt 1)

let private confirmBody (determinedDate: JsonValue) (determinedAmount: JsonValue) (version: JsonValue) : string =
    render (
        JObject
            [ "determinedDate", determinedDate
              "determinedAmount", determinedAmount
              "version", version ]
    )

let private validConfirmBody =
    confirmBody (JString "2026-04-15") (JInt 1000) (JInt 1)

let private deliverBody (deliveryDate: JsonValue) (version: JsonValue) : string =
    render (JObject [ "deliveryDate", deliveryDate; "version", version ])

let private validDeliverBody = deliverBody (JString "2026-04-15") (JInt 1)

let private designateBody
    (consignorName: JsonValue)
    (consignorCode: JsonValue)
    (designatedDate: JsonValue)
    (version: JsonValue)
    : string =
    render (
        JObject
            [ "consignorName", consignorName
              "consignorCode", consignorCode
              "designatedDate", designatedDate
              "version", version ]
    )

let private validDesignateBody =
    designateBody (JString "Alice") (JString "C-1") (JString "2026-04-15") (JInt 1)

let private resultBody (resultDate: JsonValue) (resultAmount: JsonValue) (version: JsonValue) : string =
    render (JObject [ "resultDate", resultDate; "resultAmount", resultAmount; "version", version ])

let private validResultBody = resultBody (JString "2026-04-15") (JInt 500) (JInt 1)

let private validVersionOnly = versionOnlyBody (JInt 1)

[<Collection("ApiAuthOff")>]
type ReservationConsignmentParamTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/reservation/appraisals
    // body: { appraisalDate, reservedLotInfo, reservedAmount, version }
    // ───────────────────────────────────────────────────────────────
    static member CreateReservationAppraisalCases: obj[] seq = seq {
        // 正常境界: 正しい id 形式 + 完全な body → 未登録 case で 404
        yield case nonExistentSalesCaseId validAppraisalBody 404 "not-found"
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        // version 欠落 (null) — id チェックより先に弾かれる
        yield
            case
                nonExistentSalesCaseId
                (appraisalBody (JString "2026-04-15") (JString "L-1") (JInt 1000) JNull)
                400
                "bad-request"
        // 完全に空ボディ → version 欠落で bad-request
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        // 不正 JSON → bindBody で例外 → bad-request
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case nonExistentSalesCaseId "" 400 "bad-request"
        // id フォーマット不正 — body は正常だが trySalesCaseNumber で失敗
        yield case "not-a-case" validAppraisalBody 400 "bad-request"
        yield case "2026-04" validAppraisalBody 400 "bad-request"
        yield case "abc-04-001" validAppraisalBody 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.CreateReservationAppraisalCases)>]
    member _.``POST /sales-cases/{id}/reservation/appraisals: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/reservation/appraisals" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/reservation/determine
    // body: { determinedDate, determinedAmount, version }
    // ───────────────────────────────────────────────────────────────
    static member DetermineReservationCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validConfirmBody 404 "not-found"
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        yield case nonExistentSalesCaseId (confirmBody (JString "2026-04-15") (JInt 1000) JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case "not-a-case" validConfirmBody 400 "bad-request"
        yield case "2026-04" validConfirmBody 400 "bad-request"
        yield case "abc-04-001" validConfirmBody 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.DetermineReservationCases)>]
    member _.``POST /sales-cases/{id}/reservation/determine: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/reservation/determine" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // DELETE /sales-cases/{id}/reservation/determination
    // body: { version }
    // ───────────────────────────────────────────────────────────────
    static member CancelReservationDeterminationCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        // version 欠落 → bad-request
        yield case nonExistentSalesCaseId (versionOnlyBody JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        // id フォーマット不正
        yield case "not-a-case" validVersionOnly 400 "bad-request"
        yield case "2026-04" validVersionOnly 400 "bad-request"
        yield case "abc-04-001" validVersionOnly 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.CancelReservationDeterminationCases)>]
    member _.``DELETE /sales-cases/{id}/reservation/determination: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = deleteWithBody client (sprintf "/sales-cases/%s/reservation/determination" id) (Some body)
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/reservation/delivery
    // body: { deliveryDate, version }
    // ───────────────────────────────────────────────────────────────
    static member DeliverReservationCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validDeliverBody 404 "not-found"
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        yield case nonExistentSalesCaseId (deliverBody (JString "2026-04-15") JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case "not-a-case" validDeliverBody 400 "bad-request"
        yield case "2026-04" validDeliverBody 400 "bad-request"
        yield case "abc-04-001" validDeliverBody 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.DeliverReservationCases)>]
    member _.``POST /sales-cases/{id}/reservation/delivery: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/reservation/delivery" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/consignment/designate
    // body: { consignorName, consignorCode, designatedDate, version }
    // ───────────────────────────────────────────────────────────────
    static member DesignateConsignmentCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validDesignateBody 404 "not-found"
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"

        yield
            case
                nonExistentSalesCaseId
                (designateBody (JString "Alice") (JString "C-1") (JString "2026-04-15") JNull)
                400
                "bad-request"

        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case "not-a-case" validDesignateBody 400 "bad-request"
        yield case "2026-04" validDesignateBody 400 "bad-request"
        yield case "abc-04-001" validDesignateBody 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.DesignateConsignmentCases)>]
    member _.``POST /sales-cases/{id}/consignment/designate: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/consignment/designate" id) body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // DELETE /sales-cases/{id}/consignment/designation
    // body: { version }
    // ───────────────────────────────────────────────────────────────
    static member CancelConsignmentDesignationCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        yield case nonExistentSalesCaseId (versionOnlyBody JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case "not-a-case" validVersionOnly 400 "bad-request"
        yield case "2026-04" validVersionOnly 400 "bad-request"
        yield case "abc-04-001" validVersionOnly 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.CancelConsignmentDesignationCases)>]
    member _.``DELETE /sales-cases/{id}/consignment/designation: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = deleteWithBody client (sprintf "/sales-cases/%s/consignment/designation" id) (Some body)
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/consignment/result
    // body: { resultDate, resultAmount, version }
    // ───────────────────────────────────────────────────────────────
    static member ConsignmentResultCases: obj[] seq = seq {
        yield case nonExistentSalesCaseId validResultBody 404 "not-found"
        yield case nonExistentSalesCaseId validVersionOnly 404 "not-found"
        yield case nonExistentSalesCaseId (resultBody (JString "2026-04-15") (JInt 500) JNull) 400 "bad-request"
        yield case nonExistentSalesCaseId "{}" 400 "bad-request"
        yield case nonExistentSalesCaseId "not-json" 400 "bad-request"
        yield case "not-a-case" validResultBody 400 "bad-request"
        yield case "2026-04" validResultBody 400 "bad-request"
        yield case "abc-04-001" validResultBody 400 "bad-request"
    }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof ReservationConsignmentParamTests.ConsignmentResultCases)>]
    member _.``POST /sales-cases/{id}/consignment/result: param boundaries``
        (id: string, body: string, status: int, expectedType: string)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! resp = postJson client (sprintf "/sales-cases/%s/consignment/result" id) body
            do! check status expectedType resp
        }
