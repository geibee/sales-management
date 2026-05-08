module SalesManagement.Tests.IntegrationTests.SalesCaseRetrievalTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

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

let private extractStr (root: JsonElement) (name: string) : string =
    let p = ref Unchecked.defaultof<JsonElement>
    Assert.True(root.TryGetProperty(name, p), sprintf "field '%s' missing in: %s" name (root.GetRawText()))
    (!p).GetString()

let private hasNonNull (root: JsonElement) (name: string) : bool =
    let p = ref Unchecked.defaultof<JsonElement>

    if root.TryGetProperty(name, p) then
        (!p).ValueKind <> JsonValueKind.Null
    else
        false

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
type SalesCaseRetrievalDetailTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases on missing id returns 404 problem+json``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/sales-cases/9999-99-999"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases with malformed id returns 400 problem+json``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/sales-cases/invalid-id"
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        let ct = resp.Content.Headers.ContentType
        Assert.NotNull(ct)
        Assert.Equal("application/problem+json", ct.MediaType)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``direct sales case can be retrieved with caseType=direct``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 5000 + r.Next(0, 500)
        let location = sprintf "D%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "direct" [| lotId |]

        let! resp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp
        let root = parseJson body
        Assert.Equal(caseId, extractStr root "salesCaseNumber")
        Assert.Equal("direct", extractStr root "caseType")
        Assert.Equal("before_appraisal", extractStr root "status")
        let lotsP = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("lots", lotsP))
        Assert.Equal(JsonValueKind.Array, (!lotsP).ValueKind)
        Assert.Equal(1, (!lotsP).GetArrayLength())
        Assert.Equal(lotId, (!lotsP).[0].GetString())
        let dc = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("divisionCode", dc))
        Assert.Equal(1, (!dc).GetInt32())
        Assert.Equal("2026-01-15", extractStr root "salesDate")
        let v = ref Unchecked.defaultof<JsonElement>
        Assert.True(root.TryGetProperty("version", v))
        Assert.True((!v).GetInt32() >= 1)
        // direct subtype-specific fields are present (may be null pre-state-transition)
        Assert.True(root.TryGetProperty("appraisal", ref Unchecked.defaultof<JsonElement>))
        Assert.True(root.TryGetProperty("contract", ref Unchecked.defaultof<JsonElement>))
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation sales case can be retrieved with caseType=reservation and status transitions reflect``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 5500 + r.Next(0, 500)
        let location = sprintf "E%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "reservation" [| lotId |]

        // initial GET
        let! resp1 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)
        let! body1 = readBody resp1
        let root1 = parseJson body1
        Assert.Equal("reservation", extractStr root1 "caseType")
        Assert.Equal("before_reservation", extractStr root1 "status")
        Assert.False(hasNonNull root1 "reservationPrice")

        // create reservation appraisal
        let appraisalBody =
            """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        let! aResp = postJson client (sprintf "/sales-cases/%s/reservation/appraisals" caseId) appraisalBody
        Assert.Equal(HttpStatusCode.OK, aResp.StatusCode)

        // GET reflects status + reservationPrice field present
        let! resp2 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode)
        let! body2 = readBody resp2
        let root2 = parseJson body2
        Assert.Equal("reserved", extractStr root2 "status")
        Assert.True(hasNonNull root2 "reservationPrice")
        let ea = root2.GetProperty("reservationPrice")
        Assert.Equal("2026-01-20", extractStr ea "appraisalDate")
        Assert.Equal(500000, ea.GetProperty("reservedAmount").GetInt32())

        // determine (case version was bumped to 2 by appraisal insert)
        let detBody =
            """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":2}"""

        let! dResp = postJson client (sprintf "/sales-cases/%s/reservation/determine" caseId) detBody
        Assert.Equal(HttpStatusCode.OK, dResp.StatusCode)

        let! resp3 = getReq client (sprintf "/sales-cases/%s" caseId)
        let! body3 = readBody resp3
        let root3 = parseJson body3
        Assert.Equal("reservation_confirmed", extractStr root3 "status")
        Assert.True(hasNonNull root3 "determination")
        let det = root3.GetProperty("determination")
        Assert.Equal(480000, det.GetProperty("determinedAmount").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment sales case can be retrieved with caseType=consignment``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 6000 + r.Next(0, 500)
        let location = sprintf "C%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "consignment" [| lotId |]

        let! resp1 = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode)
        let! body1 = readBody resp1
        let root1 = parseJson body1
        Assert.Equal("consignment", extractStr root1 "caseType")
        Assert.Equal("before_consignment", extractStr root1 "status")
        Assert.False(hasNonNull root1 "consignor")

        // designate consignor
        let designate =
            """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        let! dResp = postJson client (sprintf "/sales-cases/%s/consignment/designate" caseId) designate
        Assert.Equal(HttpStatusCode.OK, dResp.StatusCode)

        let! resp2 = getReq client (sprintf "/sales-cases/%s" caseId)
        let! body2 = readBody resp2
        let root2 = parseJson body2
        Assert.Equal("consignment_designated", extractStr root2 "status")
        Assert.True(hasNonNull root2 "consignor")
        let co = root2.GetProperty("consignor")
        Assert.Equal("Acme", extractStr co "consignorName")
        Assert.Equal("C001", extractStr co "consignorCode")
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseRetrieval")>]
    [<Trait("Category", "Integration")>]
    member _.``GET sales-cases does not exist for /reservation-cases or /consignment-cases``() = task {
        use client = fixture.NewClient()
        // GET to subtype-specific URLs should NOT return 200 (no GET handler registered)
        let! resp1 = getReq client "/reservation-cases/2026-01-001"
        Assert.NotEqual(HttpStatusCode.OK, resp1.StatusCode)
        let! resp2 = getReq client "/consignment-cases/2026-01-001"
        Assert.NotEqual(HttpStatusCode.OK, resp2.StatusCode)
    }
