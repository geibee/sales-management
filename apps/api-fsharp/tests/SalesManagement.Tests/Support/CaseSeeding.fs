module SalesManagement.Tests.Support.CaseSeeding

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

/// 公開 API 経由で販売案件を目的の状態まで進める seeding ヘルパ群。
/// DB スキーマへ直接依存せず、正規ルート (lot 作成 → 製造完了 → 案件作成 → …)
/// で構築する。呼び出し側が fixture.Reset() を済ませてから使う。

let private parseField (resp: HttpResponseMessage) (name: string) : Task<string> = task {
    let! body = readBody resp
    use doc = JsonDocument.Parse body
    return doc.RootElement.GetProperty(name).GetString()
}

let private parseVersion (resp: HttpResponseMessage) : Task<int> = task {
    let! body = readBody resp
    use doc = JsonDocument.Parse body
    return doc.RootElement.GetProperty("version").GetInt32()
}

/// lot を作成して製造完了まで進め、lotId を返す。
let seedManufacturedLot (client: HttpClient) (year: int) (location: string) (seq: int) : Task<string> = task {
    let body =
        createLotBody
            { emptyLotOverrides with
                Year = Some(JInt year)
                Location = Some(JString location)
                Seq = Some(JInt seq) }

    let! createResp = postJson client "/lots" body
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)
    let lotId = sprintf "%d-%s-%03d" year location seq

    let! mfgResp =
        postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) """{"date":"2026-01-10","version":1}"""

    Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
    return lotId
}

/// 指定 lot を紐付けた直販案件を作成し、salesCaseNumber を返す。
let seedDirectCase (client: HttpClient) (lotId: string) : Task<string> = task {
    let body =
        createSalesCaseBody
            { emptySalesCaseOverrides with
                Lots = Some(JArray [ JString lotId ])
                CaseType = Some(JString "direct") }

    let! resp = postJson client "/sales-cases" body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    return! parseField resp "salesCaseNumber"
}

/// `before_appraisal` 状態の直販案件を seed し `(caseId, lotId)` を返す。
let seedBeforeAppraisal (client: HttpClient) : Task<string * string> = task {
    let r = Random()
    let year = 6000 + r.Next(0, 999)
    let location = sprintf "AC%d" (r.Next(0, 999))
    let! lotId = seedManufacturedLot client year location 1
    let! caseId = seedDirectCase client lotId
    return caseId, lotId
}

/// `appraised` まで進めて `(caseId, lotId, version)` を返す。
let seedAppraised (client: HttpClient) : Task<string * string * int> = task {
    let! caseId, lotId = seedBeforeAppraisal client
    let body = directAppraisalBody lotId 1 [] []
    let! resp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let! version = parseVersion resp
    return caseId, lotId, version
}

/// `contracted` まで進めて `(caseId, version)` を返す。
let seedContracted (client: HttpClient) : Task<string * int> = task {
    let! caseId, _, version = seedAppraised client
    let body = directContractBody version [] []
    let! resp = postJson client (sprintf "/sales-cases/%s/contracts" caseId) body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let! version = parseVersion resp
    return caseId, version
}
