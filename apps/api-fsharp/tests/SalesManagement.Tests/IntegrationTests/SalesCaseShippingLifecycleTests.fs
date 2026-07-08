module SalesManagement.Tests.IntegrationTests.SalesCaseShippingLifecycleTests

open System.Net
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.CaseSeeding
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

// 直販案件の出荷指示 → 出荷完了 / 出荷指示解除の happy path (2xx)。
// StateTransitionParamTests は 400/404 境界のみを検査しているため、
// instructSalesCaseShipping / completeSalesCaseShipping /
// cancelSalesCaseShippingInstruction の 2xx 到達 — すなわち契約カバレッジ
// ラチェットへの記録とレスポンスの openapi.yaml 照合 — は本ファイルが担う
// (issue #9 §5 契約カバレッジラチェットの未到達 operation 消化)。

let private dvBody = dateVersionBody

[<Collection("ApiAuthOff")>]
type SalesCaseShippingLifecycleTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``POST shipping-instruction → shipping-completion: contracted から status が順に遷移する``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! caseId, version = seedContracted client

        let! instructResp =
            postJson
                client
                (sprintf "/sales-cases/%s/shipping-instruction" caseId)
                (dvBody "date" (JString "2026-02-10") (JInt version))

        Assert.Equal(HttpStatusCode.OK, instructResp.StatusCode)
        let! instructBody = readBody instructResp
        let instructed = parseJson instructBody
        Assert.Equal("shipping_instructed", instructed.GetProperty("status").GetString())
        Assert.Equal(caseId, instructed.GetProperty("salesCaseNumber").GetString())
        let instructedVersion = instructed.GetProperty("version").GetInt32()
        Assert.True(instructedVersion > version, "出荷指示で version が進む")

        let! completeResp =
            postJson
                client
                (sprintf "/sales-cases/%s/shipping-completion" caseId)
                (dvBody "date" (JString "2026-02-20") (JInt instructedVersion))

        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode)
        let! completeBody = readBody completeResp
        let completed = parseJson completeBody
        Assert.Equal("shipping_completed", completed.GetProperty("status").GetString())
        Assert.True(completed.GetProperty("version").GetInt32() > instructedVersion, "出荷完了で version が進む")
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``DELETE shipping-instruction: 出荷指示を解除すると contracted へ戻る``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! caseId, version = seedContracted client

        let! instructResp =
            postJson
                client
                (sprintf "/sales-cases/%s/shipping-instruction" caseId)
                (dvBody "date" (JString "2026-02-10") (JInt version))

        Assert.Equal(HttpStatusCode.OK, instructResp.StatusCode)
        let! instructBody = readBody instructResp
        let instructedVersion = (parseJson instructBody).GetProperty("version").GetInt32()

        let! cancelResp =
            deleteWithBody
                client
                (sprintf "/sales-cases/%s/shipping-instruction" caseId)
                (Some(versionOnlyBody (JInt instructedVersion)))

        Assert.Equal(HttpStatusCode.NoContent, cancelResp.StatusCode)

        // 解除後は contracted に戻っている (詳細取得で確認)
        let! detailResp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode)
        let! detailBody = readBody detailResp
        let detail = parseJson detailBody
        Assert.Equal("contracted", detail.GetProperty("status").GetString())

        let mutable instruction = Unchecked.defaultof<System.Text.Json.JsonElement>
        Assert.True(detail.TryGetProperty("shippingInstruction", &instruction))
        Assert.Equal(System.Text.Json.JsonValueKind.Null, instruction.ValueKind)
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``POST shipping-instruction: 版数不一致は 409 conflict``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! caseId, version = seedContracted client

        let! resp =
            postJson
                client
                (sprintf "/sales-cases/%s/shipping-instruction" caseId)
                (dvBody "date" (JString "2026-02-10") (JInt(version + 100)))

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode)
    }
