module SalesManagement.Tests.IntegrationTests.LotLifecycleTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private uniqueLot () =
    let r = Random()
    let year = 3000 + r.Next(0, 5000)
    let location = "T"
    let seq = r.Next(1, 999)
    let id = sprintf "%d-%s-%03d" year location seq
    year, location, seq, id

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

let private directCaseBody (lotId: string) =
    sprintf """{"lots":["%s"],"divisionCode":1,"salesDate":"2026-01-15","caseType":"direct"}""" lotId

let private lotStatus (connectionString: string) (year: int) (location: string) (seq: int) : string =
    use conn = new NpgsqlConnection(connectionString)
    conn.Open()
    use cmd = conn.CreateCommand()

    cmd.CommandText <-
        "SELECT status FROM lot WHERE lot_number_year = @y AND lot_number_location = @l AND lot_number_seq = @s"

    let py = cmd.CreateParameter()
    py.ParameterName <- "y"
    py.Value <- year
    cmd.Parameters.Add py |> ignore
    let pl = cmd.CreateParameter()
    pl.ParameterName <- "l"
    pl.Value <- location
    cmd.Parameters.Add pl |> ignore
    let ps = cmd.CreateParameter()
    ps.ParameterName <- "s"
    ps.Value <- seq
    cmd.Parameters.Add ps |> ignore

    cmd.ExecuteScalar().ToString()

[<Collection("ApiAuthOn")>]
type LotLifecycleTests(fixture: AuthOnFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot then GET retrieves it (end-to-end)``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! getResp = getReq client (sprintf "/lots/%s" lotId)
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode)
        let! body = readBody getResp
        let root = parseJson body
        Assert.Equal("manufacturing", root.GetProperty("status").GetString())
        Assert.Equal(lotId, root.GetProperty("lotNumber").GetString())

        let details = root.GetProperty("details")
        Assert.Equal(JsonValueKind.Array, details.ValueKind)
        Assert.Equal(1, details.GetArrayLength())
        let detail = details.[0]
        Assert.Equal(1.0m, detail.GetProperty("quantity").GetDecimal())
        Assert.Equal(1, detail.GetProperty("count").GetInt32())
        Assert.Equal("general", detail.GetProperty("itemCategory").GetString())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``create lot, complete manufacturing, DB row has status manufactured``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, _ = uniqueLot ()
        let lotId = sprintf "%d-%s-%03d" year location seq
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT status FROM lot WHERE lot_number_year = @y AND lot_number_location = @l AND lot_number_seq = @s"

        let py = cmd.CreateParameter()
        py.ParameterName <- "y"
        py.Value <- year
        cmd.Parameters.Add py |> ignore
        let pl = cmd.CreateParameter()
        pl.ParameterName <- "l"
        pl.Value <- location
        cmd.Parameters.Add pl |> ignore
        let ps = cmd.CreateParameter()
        ps.ParameterName <- "s"
        ps.Value <- seq
        cmd.Parameters.Add ps |> ignore

        let result = cmd.ExecuteScalar()
        Assert.NotNull(result)
        Assert.Equal("manufactured", result.ToString())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``GET non-existent lot returns 404 problem details``() = task {
        use client = fixture.NewAuthedClient [ "viewer" ]
        let! resp = getReq client "/lots/8888-Z-888"
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = readBody resp
        let root = parseJson text
        Assert.Equal("not-found", root.GetProperty("type").GetString())
        Assert.Equal(404, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``invalid state transition returns 400 problem details``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()
        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        // Lot is in 'manufacturing' state. complete-shipping requires ShippingInstructed.
        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-shipping" lotId) body
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = readBody resp
        let root = parseJson text
        Assert.Equal("invalid-state-transition", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``request without token returns 401``() = task {
        use client = fixture.NewClient()
        let! resp = getReq client "/lots/2024-A-001"
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

        let wwwAuth =
            resp.Headers.WwwAuthenticate |> Seq.map (fun h -> h.Scheme) |> List.ofSeq

        Assert.Contains("Bearer", wwwAuth)
    }

[<Collection("ApiAuthOff")>]
type LotManufacturingCancellationTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``cancel-manufacturing-completion is rejected when lot is referenced by a sales case``() = task {
        use client = fixture.NewClient()
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! mfgResp =
            postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

        Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)

        let! caseResp = postJson client "/sales-cases" (directCaseBody lotId)
        Assert.Equal(HttpStatusCode.OK, caseResp.StatusCode)
        let! caseBody = readBody caseResp
        let caseNumber = (parseJson caseBody).GetProperty("salesCaseNumber").GetString()

        // Lot is manufactured (version 2) and now referenced by the sales case.
        let! resp = postJson client (sprintf "/lots/%s/cancel-manufacturing-completion" lotId) """{"version":2}"""
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode)
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType.MediaType)
        let! text = readBody resp
        let root = parseJson text
        Assert.Equal("invalid-state-transition", root.GetProperty("type").GetString())
        Assert.Equal(400, root.GetProperty("status").GetInt32())
        let detail = root.GetProperty("detail").GetString()
        Assert.Contains("LotReferencedBySalesCase", detail)
        Assert.Contains(caseNumber, detail)

        // The lot must remain manufactured.
        Assert.Equal("manufactured", lotStatus fixture.ConnectionString year location seq)
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``cancel-manufacturing-completion succeeds after the referencing sales case is deleted``() = task {
        use client = fixture.NewClient()
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! mfgResp =
            postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

        Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)

        let! caseResp = postJson client "/sales-cases" (directCaseBody lotId)
        Assert.Equal(HttpStatusCode.OK, caseResp.StatusCode)
        let! caseBody = readBody caseResp
        let caseNumber = (parseJson caseBody).GetProperty("salesCaseNumber").GetString()

        // Delete the sales case (before_appraisal -> 204), then the lot is unreferenced.
        let! delResp = deleteWithBody client (sprintf "/sales-cases/%s" caseNumber) None
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode)

        let! resp = postJson client (sprintf "/lots/%s/cancel-manufacturing-completion" lotId) """{"version":2}"""
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
        let! text = readBody resp
        Assert.Equal("manufacturing", (parseJson text).GetProperty("status").GetString())

        Assert.Equal("manufacturing", lotStatus fixture.ConnectionString year location seq)
    }

// 出荷指示 → 出荷完了 / 品目変換指示 → 取消の happy path (2xx)。
// これらの operation は従来ステートフル PBT (LotStateMachinePropertyTests) の
// ランダム経路でしか 2xx に到達しておらず、FsCheck の seed 次第で契約カバレッジ
// (operation-coverage) の記録が run ごとに揺れていた。決定的テストで固定する。
[<Collection("ApiAuthOff")>]
type LotShippingAndConversionLifecycleTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``instruct-shipping → complete-shipping: manufactured から shipped まで遷移する``() = task {
        use client = fixture.NewClient()
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! mfgResp =
            postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

        Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
        let! mfgBody = readBody mfgResp
        let mfgVersion = (parseJson mfgBody).GetProperty("version").GetInt32()

        let! instructResp =
            postJson
                client
                (sprintf "/lots/%s/instruct-shipping" lotId)
                (sprintf """{"deadline":"2026-02-01","version":%d}""" mfgVersion)

        Assert.Equal(HttpStatusCode.OK, instructResp.StatusCode)
        let! instructBody = readBody instructResp
        let instructed = parseJson instructBody
        Assert.Equal("shipping_instructed", instructed.GetProperty("status").GetString())
        let instructedVersion = instructed.GetProperty("version").GetInt32()

        let! completeResp =
            postJson
                client
                (sprintf "/lots/%s/complete-shipping" lotId)
                (sprintf """{"date":"2026-02-10","version":%d}""" instructedVersion)

        Assert.Equal(HttpStatusCode.OK, completeResp.StatusCode)
        let! completeBody = readBody completeResp
        Assert.Equal("shipped", (parseJson completeBody).GetProperty("status").GetString())
        Assert.Equal("shipped", lotStatus fixture.ConnectionString year location seq)
    }

    [<Fact>]
    [<Trait("Category", "LotLifecycle")>]
    [<Trait("Category", "Integration")>]
    member _.``instruct-item-conversion → 取消: manufactured へ戻る``() = task {
        use client = fixture.NewClient()
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let! mfgResp =
            postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (completeManufacturingBody 1 "2026-01-10")

        Assert.Equal(HttpStatusCode.OK, mfgResp.StatusCode)
        let! mfgBody = readBody mfgResp
        let mfgVersion = (parseJson mfgBody).GetProperty("version").GetInt32()

        let! convertResp =
            postJson
                client
                (sprintf "/lots/%s/instruct-item-conversion" lotId)
                (sprintf """{"destinationItem":"変換先品目","version":%d}""" mfgVersion)

        Assert.Equal(HttpStatusCode.OK, convertResp.StatusCode)
        let! convertBody = readBody convertResp
        let converted = parseJson convertBody
        Assert.Equal("conversion_instructed", converted.GetProperty("status").GetString())
        let convertedVersion = converted.GetProperty("version").GetInt32()

        let! cancelResp =
            deleteWithBody
                client
                (sprintf "/lots/%s/instruct-item-conversion" lotId)
                (Some(sprintf """{"version":%d}""" convertedVersion))

        Assert.Equal(HttpStatusCode.OK, cancelResp.StatusCode)
        let! cancelBody = readBody cancelResp
        Assert.Equal("manufactured", (parseJson cancelBody).GetProperty("status").GetString())
        Assert.Equal("manufactured", lotStatus fixture.ConnectionString year location seq)
    }
