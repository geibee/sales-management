module SalesManagement.Tests.IntegrationTests.AppraisalContractParamTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.TheoryCases

let private case = tcase3
let private check = checkStatusAndType

// ───────────────────────────────────────────────────────────────
// Body builders for appraisal/contract endpoints
// ───────────────────────────────────────────────────────────────

let private defaultLotAppraisal (lotId: string) : JsonValue =
    JObject
        [ "lotNumber", JString lotId
          "detailAppraisals",
          JArray
              [ JObject
                    [ "detailIndex", JInt 1
                      "baseUnitPrice", JInt 1000
                      "periodAdjustmentRate", JDecimal 1m
                      "counterpartyAdjustmentRate", JDecimal 1m ] ] ]

/// 正常な normal 査定ボディを生成する。`overrides` で各フィールドを差分上書きする。
let private appraisalBody
    (lotId: string)
    (version: int)
    (overrides: (string * JsonValue) list)
    (omit: string list)
    : string =
    let defaults: (string * JsonValue) list =
        [ "type", JString "normal"
          "appraisalDate", JString "2026-01-20"
          "deliveryDate", JString "2026-01-25"
          "salesMarket", JString "market"
          "baseUnitPriceDate", JString "2026-01-01"
          "periodAdjustmentRateDate", JString "2026-01-01"
          "counterpartyAdjustmentRateDate", JString "2026-01-01"
          "taxExcludedEstimatedTotal", JInt 100000
          "lotAppraisals", JArray [ defaultLotAppraisal lotId ]
          "version", JInt version ]

    let m = Map.ofList overrides
    let omitSet = Set.ofList omit

    let fields =
        defaults
        |> List.choose (fun (k, dflt) ->
            if omitSet.Contains k then None
            else m |> Map.tryFind k |> Option.defaultValue dflt |> fun v -> Some(k, v))

    render (JObject fields)

let private contractBody (version: int) (overrides: (string * JsonValue) list) (omit: string list) : string =
    let defaults: (string * JsonValue) list =
        [ "contractDate", JString "2026-02-01"
          "person", JString "person"
          "buyer", JObject [ "customerNumber", JString "CUST001"; "agentName", JString "agent" ]
          "salesType", JInt 1
          "item", JString "item"
          "deliveryMethod", JString "method"
          "paymentDeferralCondition", JString ""
          "salesMethod", JInt 1
          "usage", JString ""
          "taxExcludedContractAmount", JInt 100000
          "consumptionTax", JInt 10000
          "taxExcludedPaymentAmount", JInt 100000
          "paymentConsumptionTax", JInt 10000
          "version", JInt version ]

    let m = Map.ofList overrides
    let omitSet = Set.ofList omit

    let fields =
        defaults
        |> List.choose (fun (k, dflt) ->
            if omitSet.Contains k then None
            else m |> Map.tryFind k |> Option.defaultValue dflt |> fun v -> Some(k, v))

    render (JObject fields)

let private versionOnly (v: JsonValue) : string = render (JObject [ "version", v ])

// ───────────────────────────────────────────────────────────────
// Seeding helpers — drive lot → manufactured → sales case → appraisal
// via the public API so we don't depend on the DB schema directly.
// ───────────────────────────────────────────────────────────────

let private parseField (resp: HttpResponseMessage) (name: string) : Task<string> =
    task {
        let! body = readBody resp
        use doc = JsonDocument.Parse body
        return doc.RootElement.GetProperty(name).GetString()
    }

let private seedManufacturedLot (client: HttpClient) (year: int) (location: string) (seq: int) : Task<string> =
    task {
        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt year)
                    Location = Some(JString location)
                    Seq = Some(JInt seq) }

        let! createResp = postJson client "/lots" body
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)
        let lotId = sprintf "%d-%s-%d" year location seq

        let! mfgResp =
            postJson
                client
                (sprintf "/lots/%s/complete-manufacturing" lotId)
                """{"date":"2026-01-10","version":1}"""

        Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
        return lotId
    }

let private seedDirectCase (client: HttpClient) (lotId: string) : Task<string> =
    task {
        let body =
            createSalesCaseBody
                { emptySalesCaseOverrides with
                    Lots = Some(JArray [ JString lotId ])
                    CaseType = Some(JString "direct") }

        let! resp = postJson client "/sales-cases" body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        return! parseField resp "salesCaseNumber"
    }

/// Seed `(caseId, lotId)` for a sales case in `before_appraisal` state.
/// Caller is responsible for `fixture.Reset()` before invoking.
let private seedBeforeAppraisal (client: HttpClient) : Task<string * string> =
    task {
        let r = Random()
        let year = 6000 + r.Next(0, 999)
        let location = sprintf "AC%d" (r.Next(0, 999))
        let! lotId = seedManufacturedLot client year location 1
        let! caseId = seedDirectCase client lotId
        return caseId, lotId
    }

/// Drive a freshly seeded direct case to `appraised` and return `(caseId, lotId, version)`.
let private seedAppraised (client: HttpClient) : Task<string * string * int> =
    task {
        let! caseId, lotId = seedBeforeAppraisal client
        let body = appraisalBody lotId 1 [] []
        let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        use doc = JsonDocument.Parse body
        let version = doc.RootElement.GetProperty("version").GetInt32()
        return caseId, lotId, version
    }

// `Invalid sales case id` / `Sales case not found` 等は ProblemDetails の
// `type` に対応する。`bad-request` (400) と `not-found` (404) を区別するため、
// `expectedType` 列で型を明示する。

[<Collection("ApiAuthOff")>]
type AppraisalContractParamTests(fixture: AuthOffFixture) =

    // ───────────────────────────────────────────────────────────────
    // POST /sales-cases/{id}/appraisals — id format boundaries
    // 形式不正は 400 bad-request、形式は正しいが未登録は 404 not-found。
    // ───────────────────────────────────────────────────────────────
    static member AppraisalIdCases: obj[] seq =
        seq {
            // 形式正しい未登録
            yield case "9999-99-999" 404 "not-found"
            yield case "1-1-1" 404 "not-found"
            // 3 パートだが parse 不能
            yield case "abc-def-ghi" 400 "bad-request"
            yield case "2026-01-xyz" 400 "bad-request"
            // パート数違い
            yield case "2026-01" 400 "bad-request"
            yield case "2026-01-1-extra" 400 "bad-request"
            yield case "not-a-case-id" 400 "bad-request"
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalIdCases)>]
    member _.``POST /sales-cases/{id}/appraisals: id format boundaries``
        (id: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            // 有効ボディを送って id 検証だけが効くようにする
            let body = appraisalBody "0001-A-1" 1 [] []
            let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" id) body
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalIdCases)>]
    member _.``PUT /sales-cases/{id}/appraisals: id format boundaries``
        (id: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let body = appraisalBody "0001-A-1" 1 [] []
            let! resp = putJson client (sprintf "/sales-cases/%s/appraisals" id) body
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalIdCases)>]
    member _.``DELETE /sales-cases/{id}/appraisals: id format boundaries``
        (id: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp =
                deleteWithBody client (sprintf "/sales-cases/%s/appraisals" id) (Some(versionOnly (JInt 1)))

            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalIdCases)>]
    member _.``POST /sales-cases/{id}/contracts: id format boundaries``
        (id: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let body = contractBody 1 [] []
            let! resp = postJson client (sprintf "/sales-cases/%s/contracts" id) body
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalIdCases)>]
    member _.``DELETE /sales-cases/{id}/contracts: id format boundaries``
        (id: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp =
                deleteWithBody client (sprintf "/sales-cases/%s/contracts" id) (Some(versionOnly (JInt 1)))

            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // version 必須フィールドの境界
    // null / 欠落 → 400 "version is required"; non-int は body parse 失敗で 400
    // 検証は state lookup より前なので未登録 id でも 400 が出る。
    // ───────────────────────────────────────────────────────────────

    static member AppraisalVersionCases: obj[] seq =
        let validBody = appraisalBody "0001-A-1" 1 [] []
        seq {
            // 正常 → state は不適合だが id は valid format/未登録なので 404
            yield case validBody 404 "not-found"
            // version null
            yield case (appraisalBody "0001-A-1" 1 [ "version", JNull ] []) 400 "bad-request"
            // version 欠落
            yield case (appraisalBody "0001-A-1" 1 [] [ "version" ]) 400 "bad-request"
            // version 文字列 → BindJsonAsync が失敗して "Invalid body"
            yield case (appraisalBody "0001-A-1" 1 [ "version", JString "abc" ] []) 400 "bad-request"
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalVersionCases)>]
    member _.``POST /sales-cases/{id}/appraisals: version boundaries``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = postJson client "/sales-cases/9999-99-999/appraisals" body
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.AppraisalVersionCases)>]
    member _.``PUT /sales-cases/{id}/appraisals: version boundaries``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = putJson client "/sales-cases/9999-99-999/appraisals" body
            do! check status expectedType resp
        }

    static member DeleteVersionCases: obj[] seq =
        seq {
            yield case (versionOnly (JInt 1)) 404 "not-found"
            yield case (versionOnly JNull) 400 "bad-request"
            yield case (render (JObject [])) 400 "bad-request"
            yield case (versionOnly (JString "abc")) 400 "bad-request"
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.DeleteVersionCases)>]
    member _.``DELETE /sales-cases/{id}/appraisals: version boundaries``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = deleteWithBody client "/sales-cases/9999-99-999/appraisals" (Some body)
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.DeleteVersionCases)>]
    member _.``DELETE /sales-cases/{id}/contracts: version boundaries``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = deleteWithBody client "/sales-cases/9999-99-999/contracts" (Some body)
            do! check status expectedType resp
        }

    static member ContractVersionCases: obj[] seq =
        seq {
            yield case (contractBody 1 [] []) 404 "not-found"
            yield case (contractBody 1 [ "version", JNull ] []) 400 "bad-request"
            yield case (contractBody 1 [] [ "version" ]) 400 "bad-request"
            yield case (contractBody 1 [ "version", JString "abc" ] []) 400 "bad-request"
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.ContractVersionCases)>]
    member _.``POST /sales-cases/{id}/contracts: version boundaries``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = postJson client "/sales-cases/9999-99-999/contracts" body
            do! check status expectedType resp
        }

    // ───────────────────────────────────────────────────────────────
    // Body parse boundaries — 完全に壊れた JSON / 空 body
    // ───────────────────────────────────────────────────────────────
    static member BadBodyCases: obj[] seq =
        seq {
            yield case "not json" 400 "bad-request"
            yield case "" 400 "bad-request"
            yield case "{" 400 "bad-request"
            yield case "[]" 400 "bad-request"
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.BadBodyCases)>]
    member _.``POST /sales-cases/{id}/appraisals: malformed body``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = postJson client "/sales-cases/9999-99-999/appraisals" body
            do! check status expectedType resp
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<MemberData(nameof AppraisalContractParamTests.BadBodyCases)>]
    member _.``POST /sales-cases/{id}/contracts: malformed body``
        (body: string, status: int, expectedType: string)
        =
        task {
            use client = fixture.NewClient()
            let! resp = postJson client "/sales-cases/9999-99-999/contracts" body
            do! check status expectedType resp
        }

// ───────────────────────────────────────────────────────────────
// State-seeded boundary tests — `before_appraisal` / `appraised` を
// 正規ルートで構築し、その上で param 境界を検証する。
// 1 test = 1 seed なので Reset を必ず呼ぶ。
// ───────────────────────────────────────────────────────────────
[<Collection("ApiAuthOff")>]
type AppraisalContractStatefulParamTests(fixture: AuthOffFixture) =

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData("normal", 200)>]
    [<InlineData("customer_contract", 400)>] // customerContractNumber/Rate 未指定なので 400
    [<InlineData("unknown", 400)>]
    [<InlineData("", 400)>]
    [<InlineData("NORMAL", 400)>]
    member _.``POST /sales-cases/{id}/appraisals: type enum``(typ: string, status: int) =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, lotId = seedBeforeAppraisal client
            let body = appraisalBody lotId 1 [ "type", JString typ ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    // DateOnly.TryParse は invariant culture で柔軟にパースし、`yyyy/MM/dd` も許容する。
    // ここでは「明確に日付でない」入力のみ 400 として検証する。
    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData("2026-01-20", 200)>]
    [<InlineData("2026-12-31", 200)>]
    [<InlineData("not-a-date", 400)>]
    [<InlineData("13-99", 400)>]
    [<InlineData("", 400)>]
    member _.``POST /sales-cases/{id}/appraisals: appraisalDate format``
        (date: string, status: int)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, lotId = seedBeforeAppraisal client
            let body = appraisalBody lotId 1 [ "appraisalDate", JString date ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    // Amount.tryCreate は >= 0 を許容し、負値のみ 400 を返す（境界の最低値が 0）。
    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData(0, 200)>]
    [<InlineData(1, 200)>]
    [<InlineData(-1, 400)>]
    [<InlineData(-100, 400)>]
    member _.``POST /sales-cases/{id}/appraisals: taxExcludedEstimatedTotal boundaries``
        (total: int, status: int)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, lotId = seedBeforeAppraisal client
            let body = appraisalBody lotId 1 [ "taxExcludedEstimatedTotal", JInt total ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData(1, 200)>]
    [<InlineData(2, 400)>] // detailIndex 2 は 1 件しかない baseline では out of range
    [<InlineData(0, 400)>]
    member _.``POST /sales-cases/{id}/appraisals: detailIndex out of range``
        (idx: int, status: int)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, lotId = seedBeforeAppraisal client

            let lotAppraisals =
                JArray
                    [ JObject
                          [ "lotNumber", JString lotId
                            "detailAppraisals",
                            JArray
                                [ JObject
                                      [ "detailIndex", JInt idx
                                        "baseUnitPrice", JInt 1000
                                        "periodAdjustmentRate", JDecimal 1m
                                        "counterpartyAdjustmentRate", JDecimal 1m ] ] ] ]

            let body = appraisalBody lotId 1 [ "lotAppraisals", lotAppraisals ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData("2026-02-01", 200)>]
    [<InlineData("not-a-date", 400)>]
    [<InlineData("13-99", 400)>]
    [<InlineData("", 400)>]
    member _.``POST /sales-cases/{id}/contracts: contractDate format``
        (date: string, status: int)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, _, version = seedAppraised client
            let body = contractBody version [ "contractDate", JString date ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/contracts" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    // Amount.tryCreate は >= 0 を許容する。負値のみ 400 を返す。
    [<Theory>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    [<InlineData(0, 200)>]
    [<InlineData(1, 200)>]
    [<InlineData(-1, 400)>]
    [<InlineData(-100, 400)>]
    member _.``POST /sales-cases/{id}/contracts: taxExcludedContractAmount boundaries``
        (amount: int, status: int)
        =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, _, version = seedAppraised client
            let body = contractBody version [ "taxExcludedContractAmount", JInt amount ] []
            let! resp = postJson client (sprintf "/sales-cases/%s/contracts" caseId) body
            Assert.Equal(enum<HttpStatusCode> status, resp.StatusCode)
        }

    // appraised 状態でない案件に PUT/DELETE appraisals すると 400 を返す
    [<Fact>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    member _.``PUT /sales-cases/{id}/appraisals: requires appraised state``() =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, lotId = seedBeforeAppraisal client
            let body = appraisalBody lotId 1 [] []
            let! resp = putJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        }

    [<Fact>]
    [<Trait("Category", "Param")>]
    [<Trait("Category", "Integration")>]
    member _.``DELETE /sales-cases/{id}/contracts: requires contracted state``() =
        task {
            fixture.Reset()
            use client = fixture.NewClient()
            let! caseId, _, version = seedAppraised client
            let! resp =
                deleteWithBody
                    client
                    (sprintf "/sales-cases/%s/contracts" caseId)
                    (Some(versionOnly (JInt version)))

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        }
