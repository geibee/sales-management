module SalesManagement.Tests.IntegrationTests.OutboxTests

open System
open System.Net
open System.Threading.Tasks
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers

let private uniqueLot () =
    let r = Random()
    let year = 4000 + r.Next(0, 4000)
    let location = "E"
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

[<Collection("ApiAuthOn")>]
type OutboxTests(fixture: AuthOnFixture) =
    let queryOutboxRow (lotId: string) : (int64 * string * string * string) option =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT id, event_type, status, payload::text FROM outbox_events WHERE payload->>'lotId' = @lid ORDER BY id DESC LIMIT 1"

        let p = cmd.CreateParameter()
        p.ParameterName <- "lid"
        p.Value <- lotId
        cmd.Parameters.Add p |> ignore
        use reader = cmd.ExecuteReader()

        if reader.Read() then
            Some(reader.GetInt64 0, reader.GetString 1, reader.GetString 2, reader.GetString 3)
        else
            None

    let queryLotStatus (year: int) (location: string) (seq: int) : string option =
        use conn = new NpgsqlConnection(fixture.ConnectionString)
        conn.Open()
        use cmd = conn.CreateCommand()

        cmd.CommandText <-
            "SELECT status FROM lot WHERE lot_number_year = @y AND lot_number_location = @l AND lot_number_seq = @s"

        let p1 = cmd.CreateParameter()
        p1.ParameterName <- "y"
        p1.Value <- year
        cmd.Parameters.Add p1 |> ignore
        let p2 = cmd.CreateParameter()
        p2.ParameterName <- "l"
        p2.Value <- location
        cmd.Parameters.Add p2 |> ignore
        let p3 = cmd.CreateParameter()
        p3.ParameterName <- "s"
        p3.Value <- seq
        cmd.Parameters.Add p3 |> ignore
        let result = cmd.ExecuteScalar()

        if isNull result || (result :? DBNull) then
            None
        else
            Some(result.ToString())

    [<Fact>]
    [<Trait("Category", "Outbox")>]
    [<Trait("Category", "Integration")>]
    member _.``complete-manufacturing inserts outbox event in same transaction``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        // Phase 2-1: outbox row exists with event_type=LotManufacturingCompleted
        let outboxRow = queryOutboxRow lotId
        Assert.True(outboxRow.IsSome, sprintf "outbox row missing for %s" lotId)
        let _, eventType, _, payload = outboxRow.Value
        Assert.Equal("LotManufacturingCompleted", eventType)
        Assert.Contains(lotId, payload)

        // Phase 2-2: lot table updated AND outbox has the row (same transaction)
        let lotStatus = queryLotStatus year location seq
        Assert.Equal(Some "manufactured", lotStatus)
    }

    [<Fact>]
    [<Trait("Category", "Outbox")>]
    [<Trait("Category", "Integration")>]
    member _.``background processor flips outbox status from pending to processed``() = task {
        use client = fixture.NewAuthedClient [ "operator" ]
        let year, location, seq, lotId = uniqueLot ()

        let! createResp = postJson client "/lots" (lotBody year location seq)
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode)

        let body = """{"date": "2026-04-22", "version": 1}"""
        let! resp = postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

        // Phase 3: poll until status becomes 'processed' (poll interval = 500ms)
        let deadline = DateTime.UtcNow.AddSeconds(15.0)
        let mutable finalStatus = "pending"

        while finalStatus <> "processed" && DateTime.UtcNow < deadline do
            do! Task.Delay 300

            match queryOutboxRow lotId with
            | Some(_, _, status, _) -> finalStatus <- status
            | None -> ()

        Assert.Equal("processed", finalStatus)
    }
