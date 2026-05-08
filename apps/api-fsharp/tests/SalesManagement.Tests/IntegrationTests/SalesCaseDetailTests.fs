module SalesManagement.Tests.IntegrationTests.SalesCaseDetailTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private extractStr (root: JsonElement) (name: string) : string =
    let p = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty(name, p), sprintf "field '%s' missing in: %s" name (root.GetRawText()))
    (!p).GetString()

let private lotBody (year: int) (location: string) (seq: int) =
    sprintf
        """{
            "lotNumber": {"year": %d, "location": "%s", "seq": %d},
            "divisionCode": 1, "departmentCode": 1, "sectionCode": 1,
            "processCategory": 1, "inspectionCategory": 1, "manufacturingCategory": 1,
            "details": [
                {"itemCategory": "general", "premiumCategory": "", "productCategoryCode": "v",
                 "lengthSpecLower": 1.0, "thicknessSpecLower": 1.0, "thicknessSpecUpper": 2.0,
                 "qualityGrade": "A", "count": 1, "quantity": 1.0, "inspectionResultCategory": ""}
            ]
        }"""
        year
        location
        seq

let private completeManufacturingBody (version: int) (date: string) =
    sprintf """{"date":"%s","version":%d}""" date version

let private setupManufacturedLot (client: HttpClient) (year: int) (location: string) (seq: int) : Task<string> = task {
    let! createResp = postJson client "/lots" (lotBody year location seq)
    Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)
    let lotId = sprintf "%d-%s-%03d" year location seq

    let! mfgResp =
        postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

    Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
    return lotId
}

let private createCase (client: HttpClient) (caseType: string) (lotIds: string[]) : Task<string> = task {
    let lotsJson = String.Join(",", lotIds |> Array.map (sprintf "\"%s\""))

    let body =
        sprintf """{"lots":[%s],"divisionCode":1,"salesDate":"2026-01-15","caseType":"%s"}""" lotsJson caseType

    let! resp = postJson client "/sales-cases" body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let! responseBody = readBody resp
    let root = parseJson responseBody
    return extractStr root "salesCaseNumber"
}

[<Collection("ApiAuthOff")>]
type SalesCaseDetailOpenApiSpecTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml declares getSalesCase under /sales-cases/{id}``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        Assert.Contains("operationId: getSalesCase", body)
        Assert.Contains("SalesCaseDetailResponse", body)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml SalesCaseDetailResponse lists required and subtype fields``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        let! body = readBody resp

        // 必須フィールド
        for f in
            [ "salesCaseNumber"
              "caseType"
              "status"
              "lots"
              "divisionCode"
              "salesDate"
              "version" ] do
            Assert.Contains(f, body)

        // direct 案件サブタイプフィールド
        for f in [ "appraisal"; "contract"; "shippingInstruction"; "shippingCompletion" ] do
            Assert.Contains(f, body)

        // reservation 案件サブタイプフィールド
        for f in [ "reservationPrice"; "determination"; "delivery" ] do
            Assert.Contains(f, body)

        // consignment 案件サブタイプフィールド
        for f in [ "consignor"; "result" ] do
            Assert.Contains(f, body)
    }

[<Collection("ApiAuthOff")>]
type DetailRetrievalTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``GET /sales-cases/{id} returns 404 for unknown id``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/sales-cases/9999-99-999"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``direct sales case returns caseType=direct``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 7000 + r.Next(0, 500)
        let location = sprintf "DA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "direct" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("direct", extractStr root "caseType")
        Assert.Equal(caseId, extractStr root "salesCaseNumber")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation sales case returns caseType=reservation``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 7500 + r.Next(0, 500)
        let location = sprintf "EA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "reservation" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("reservation", extractStr root "caseType")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseDetail")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment sales case returns caseType=consignment``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 8000 + r.Next(0, 500)
        let location = sprintf "CA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "consignment" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal("consignment", extractStr root "caseType")
    }
