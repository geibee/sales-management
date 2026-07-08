module SalesManagement.Tests.IntegrationTests.SalesCaseAppraisalLifecycleTests

open System.Net
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.CaseSeeding
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

// 査定の更新/削除・契約の削除の happy path (2xx)。
// これらの operation は従来ステートフル PBT (SalesCaseStateMachinePropertyTests)
// のランダム経路でしか 2xx に到達しておらず、FsCheck の seed 次第で契約カバレッジ
// (operation-coverage) の記録が run ごとに揺れていた。決定的テストで固定する。

[<Collection("ApiAuthOff")>]
type SalesCaseAppraisalLifecycleTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``PUT appraisals → DELETE appraisals: 査定を更新し、削除で before_appraisal へ戻る``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! caseId, lotId, version = seedAppraised client

        let! updateResp =
            putJson client (sprintf "/sales-cases/%s/appraisals" caseId) (directAppraisalBody lotId version [] [])

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode)
        let! updateBody = readBody updateResp
        let updatedVersion = (parseJson updateBody).GetProperty("version").GetInt32()
        Assert.True(updatedVersion > version, "査定更新で version が進む")

        let! deleteResp =
            deleteWithBody
                client
                (sprintf "/sales-cases/%s/appraisals" caseId)
                (Some(versionOnlyBody (JInt updatedVersion)))

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode)

        let! detailResp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode)
        let! detailBody = readBody detailResp
        Assert.Equal("before_appraisal", (parseJson detailBody).GetProperty("status").GetString())
    }

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``DELETE contracts: 契約を削除すると appraised へ戻る``() = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let! caseId, version = seedContracted client

        let! deleteResp =
            deleteWithBody client (sprintf "/sales-cases/%s/contracts" caseId) (Some(versionOnlyBody (JInt version)))

        Assert.Equal(HttpStatusCode.NoContent, deleteResp.StatusCode)

        let! detailResp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode)
        let! detailBody = readBody detailResp
        Assert.Equal("appraised", (parseJson detailBody).GetProperty("status").GetString())
    }
