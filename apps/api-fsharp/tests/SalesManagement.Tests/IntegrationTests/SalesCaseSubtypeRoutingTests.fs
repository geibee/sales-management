module SalesManagement.Tests.IntegrationTests.SalesCaseSubtypeRoutingTests

open System
open System.Net
open System.Net.Http
open System.Text
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
type ReservationUrlTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation mutations work under /sales-cases/{id}/reservation/...``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 8500 + r.Next(0, 100)
        let location = sprintf "S18E%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "reservation" [| lotId |]

        // reservation appraisal
        let! appraisalResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/appraisals" caseId)
                """{"appraisalDate":"2026-01-20","reservedLotInfo":"info","reservedAmount":500000,"version":1}"""

        Assert.Equal(HttpStatusCode.OK, appraisalResp.StatusCode)

        // reservation determine
        let! determineResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":2}"""

        Assert.Equal(HttpStatusCode.OK, determineResp.StatusCode)

        // reservation cancel determination (DELETE /determination)
        let! cancelResp =
            deleteWithBody
                client
                (sprintf "/sales-cases/%s/reservation/determination" caseId)
                (Some """{"version":3}""")

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode)

        // re-determine and then deliver
        let! redoResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/determine" caseId)
                """{"determinedDate":"2026-01-22","determinedAmount":480000,"version":4}"""

        Assert.Equal(HttpStatusCode.OK, redoResp.StatusCode)

        let! deliverResp =
            postJson
                client
                (sprintf "/sales-cases/%s/reservation/delivery" caseId)
                """{"deliveryDate":"2026-01-30","version":5}"""

        Assert.Equal(HttpStatusCode.OK, deliverResp.StatusCode)

        // Persistence regression guard: GET must echo the deliveryDate the client posted.
        let! detailResp = getReq client (sprintf "/sales-cases/%s" caseId)
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode)
        let! detailBody = readBody detailResp
        let detailRoot = parseJson detailBody
        let delivery = detailRoot.GetProperty "delivery"
        Assert.Equal(JsonValueKind.Object, delivery.ValueKind)
        Assert.Equal("2026-01-30", delivery.GetProperty("deliveredDate").GetString())
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``old /reservation-cases/{id}/... URLs return 404``() = task {
        use client = fixture.NewClient()

        let probe (method: HttpMethod) (path: string) : Task<HttpStatusCode> = task {
            let req = new HttpRequestMessage(method, path)
            req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            let! resp = client.SendAsync req
            return resp.StatusCode
        }

        let! s1 = probe HttpMethod.Post "/reservation-cases/2026-01-001/appraisals"
        Assert.Equal(HttpStatusCode.NotFound, s1)
        let! s2 = probe HttpMethod.Post "/reservation-cases/2026-01-001/determine"
        Assert.Equal(HttpStatusCode.NotFound, s2)
        let! s3 = probe HttpMethod.Delete "/reservation-cases/2026-01-001/determination"
        Assert.Equal(HttpStatusCode.NotFound, s3)
        let! s4 = probe HttpMethod.Post "/reservation-cases/2026-01-001/delivery"
        Assert.Equal(HttpStatusCode.NotFound, s4)
    }

[<Collection("ApiAuthOff")>]
type ConsignmentUrlTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment mutations work under /sales-cases/{id}/consignment/...``() = task {
        use client = fixture.NewClient()
        let r = Random()
        let year = 8700 + r.Next(0, 100)
        let location = sprintf "S18C%d" (r.Next(0, 999))
        let! lotId = setupManufacturedLot client year location 1
        let! caseId = createCase client "consignment" [| lotId |]

        // consignment designate
        let! designateResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":1}"""

        Assert.Equal(HttpStatusCode.OK, designateResp.StatusCode)

        // consignment cancel designation (DELETE /designation)
        let! cancelResp =
            deleteWithBody
                client
                (sprintf "/sales-cases/%s/consignment/designation" caseId)
                (Some """{"version":2}""")

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode)

        // re-designate and then record result
        let! redoResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/designate" caseId)
                """{"consignorName":"Acme","consignorCode":"C001","designatedDate":"2026-01-25","version":3}"""

        Assert.Equal(HttpStatusCode.OK, redoResp.StatusCode)

        let! resultResp =
            postJson
                client
                (sprintf "/sales-cases/%s/consignment/result" caseId)
                """{"resultDate":"2026-01-30","resultAmount":480000,"version":4}"""

        Assert.Equal(HttpStatusCode.OK, resultResp.StatusCode)
    }

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``old /consignment-cases/{id}/... URLs return 404``() = task {
        use client = fixture.NewClient()

        let probe (method: HttpMethod) (path: string) : Task<HttpStatusCode> = task {
            let req = new HttpRequestMessage(method, path)
            req.Content <- new StringContent("{}", Encoding.UTF8, "application/json")
            let! resp = client.SendAsync req
            return resp.StatusCode
        }

        let! s1 = probe HttpMethod.Post "/consignment-cases/2026-01-001/designate"
        Assert.Equal(HttpStatusCode.NotFound, s1)
        let! s2 = probe HttpMethod.Delete "/consignment-cases/2026-01-001/designation"
        Assert.Equal(HttpStatusCode.NotFound, s2)
        let! s3 = probe HttpMethod.Post "/consignment-cases/2026-01-001/result"
        Assert.Equal(HttpStatusCode.NotFound, s3)
    }

[<Collection("ApiAuthOff")>]
type SubtypeRoutingOpenApiSpecTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "SalesCaseSubtypeRouting")>]
    [<Trait("Category", "Integration")>]
    member _.``openapi.yaml exposes new sales-cases reservation/consignment paths and not the old ones``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/openapi.yaml"
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! body = readBody resp

        Assert.Contains("/sales-cases/{id}/reservation/appraisals", body)
        Assert.Contains("/sales-cases/{id}/reservation/determine", body)
        Assert.Contains("/sales-cases/{id}/reservation/determination", body)
        Assert.Contains("/sales-cases/{id}/reservation/delivery", body)
        Assert.Contains("/sales-cases/{id}/consignment/designate", body)
        Assert.Contains("/sales-cases/{id}/consignment/designation", body)
        Assert.Contains("/sales-cases/{id}/consignment/result", body)

        Assert.DoesNotContain("\n  /reservation-cases/", body)
        Assert.DoesNotContain("\n  /consignment-cases/", body)
    }
