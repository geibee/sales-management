module SalesManagement.Tests.IntegrationTests.OptimisticLockConflictTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private extractStr (root: System.Text.Json.JsonElement) (name: string) : string =
    let mutable p = Unchecked.defaultof<System.Text.Json.JsonElement>
    Assert.True(root.TryGetProperty(name, &p), sprintf "field '%s' missing in: %s" name (root.GetRawText()))
    p.GetString()

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

let private createSalesCase (client: HttpClient) (caseType: string) (lotIds: string[]) : Task<string> = task {
    let lotsJson = String.Join(",", lotIds |> Array.map (sprintf "\"%s\""))

    let body =
        sprintf """{"lots":[%s],"divisionCode":1,"salesDate":"2026-01-15","caseType":"%s"}""" lotsJson caseType

    let! resp = postJson client "/sales-cases" body
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let! text = readBody resp
    let root = parseJson text
    return extractStr root "salesCaseNumber"
}

let private appraisalBody (lotId: string) (version: int) =
    sprintf
        """{"type":"normal","appraisalDate":"2026-01-20","deliveryDate":"2026-01-25",
            "salesMarket":"market","baseUnitPriceDate":"2026-01-01",
            "periodAdjustmentRateDate":"2026-01-01","counterpartyAdjustmentRateDate":"2026-01-01",
            "taxExcludedEstimatedTotal":100000,
            "lotAppraisals":[{"lotNumber":"%s","detailAppraisals":[{"detailIndex":1,"baseUnitPrice":1000,"periodAdjustmentRate":1.0,"counterpartyAdjustmentRate":1.0}]}],
            "version":%d}"""
        lotId
        version

[<Collection("ApiAuthOff")>]
type SalesCaseConflictTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``PUT sales-cases appraisals with stale version returns 409``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 9000 + r.Next(0, 500)
        let location = sprintf "VA%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "direct" [| lotId |]

        // First appraisal with version=1 → should succeed (case version becomes 2)
        let! firstResp = postJson client (sprintf "/sales-cases/%s/appraisals" caseId) (appraisalBody lotId 1)
        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)
        let! firstText = readBody firstResp
        let firstRoot = parseJson firstText
        Assert.Equal(2, firstRoot.GetProperty("version").GetInt32())

        // PUT update with stale version=1 → should be 409
        let! conflictResp = putJson client (sprintf "/sales-cases/%s/appraisals" caseId) (appraisalBody lotId 1)
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

[<Collection("ApiAuthOff")>]
type ReservationConflictTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``POST sales-cases reservation appraisals with stale version returns 409``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 9500 + r.Next(0, 500)
        let location = sprintf "VE%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "reservation" [| lotId |]

        // First insert with version=1 → success
        let! firstResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/appraisals" caseId)
                """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)

        // Determination request with stale version=1 (case is now at version=2) → 409
        let! conflictResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":1}"""

        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

[<Collection("ApiAuthOff")>]
type ConsignmentConflictTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "OptimisticLockConflict")>]
    [<Trait("Category", "Integration")>]
    member _.``POST sales-cases consignment designate twice with stale version returns 409``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 9700 + r.Next(0, 200)
        let location = sprintf "VC%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createSalesCase client "consignment" [| lotId |]

        // First designate with version=1 → success
        let! firstResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        Assert.Equal(HttpStatusCode.OK, firstResp.StatusCode)

        // Result with stale version=1 (case is now at version=2) → 409
        let! conflictResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/result" caseId)
                """{"resultDate":"2026-01-30","resultAmount":480000,"version":1}"""

        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode)
        let ct = conflictResp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }
