module SalesManagement.Tests.IntegrationTests.LotTransitionMatrixTests

open System
open System.IO
open System.Net
open System.Net.Http
open System.Text.Json
open System.Threading
open Npgsql
open Xunit
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.ProblemDetailsAssert
open SalesManagement.Tests.Support.OpenApiValidation
open SalesManagement.Tests.Support.RequestBuilders

// Javaと同じcharacterization表を読み、実装同士の一致ではなく期待値を独立に照合する。
let private matrix =
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "lot-transitions.json"))
    |> parseJson

let private states = matrix.GetProperty("states")
let private actions = matrix.GetProperty("actions")
let mutable private nextSequence = 0

let private sendAction (client: HttpClient) (lotId: string) (action: string) (version: int) =
    let definition = actions.GetProperty action

    let body =
        [ for field in definition.GetProperty("body").EnumerateObject() do
              yield field.Name, JRaw(field.Value.GetRawText())
          yield "version", JInt version ]
        |> JObject
        |> render

    let path = sprintf "/lots/%s/%s" lotId (definition.GetProperty("path").GetString())

    match definition.GetProperty("method").GetString() with
    | "POST" -> postJson client path body
    | "DELETE" -> deleteWithBody client path (Some body)
    | other -> failwithf "未知の遷移メソッド: %s" other

[<Collection("ApiAuthOff")>]
type LotTransitionMatrixTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    // GETキャッシュだけを比較して、DBの誤更新を見逃さないための独立スナップショット。
    let snapshot (sequence: int) (lotId: string) =
        use connection = new NpgsqlConnection(fixture.ConnectionString)
        connection.Open()
        use command = connection.CreateCommand()

        command.CommandText <-
            """SELECT jsonb_build_object(
                'lot', (SELECT to_jsonb(l) FROM lot l WHERE lot_number_year=2026 AND lot_number_location='XR' AND lot_number_seq=@sequence),
                'details', (SELECT jsonb_agg(to_jsonb(d) ORDER BY seq_no) FROM lot_detail d WHERE lot_number_year=2026 AND lot_number_location='XR' AND lot_number_seq=@sequence),
                'events', (SELECT count(*) FROM outbox_events WHERE payload->>'lotId'=@lotId))::text"""

        command.Parameters.AddWithValue("sequence", sequence) |> ignore
        command.Parameters.AddWithValue("lotId", lotId) |> ignore
        command.ExecuteScalar() :?> string

    static member Cases: obj[] seq = seq {
        for state in states.EnumerateObject() do
            for action in actions.EnumerateObject() do
                yield [| box state.Name; box action.Name; box false |]

                if fst (state.Value.GetProperty("allowed").TryGetProperty action.Name) then
                    yield [| box state.Name; box action.Name; box true |]
    }

    [<Fact>]
    [<Trait("Category", "Param")>]
    member _.``XR-LOT-000 共通表は契約の全ロット状態と遷移操作を含む``() =
        let document = specDocument ()

        let expectedStates =
            document.Components.Schemas.["LotStatus"].Enum
            |> Seq.map (fun value -> (value :?> Microsoft.OpenApi.Any.OpenApiString).Value)
            |> Set.ofSeq

        let actualStates =
            states.EnumerateObject() |> Seq.map (fun state -> state.Name) |> Set.ofSeq

        Assert.Equal<Set<string>>(expectedStates, actualStates)

        let expectedActions =
            document.Paths
            |> Seq.filter (fun entry -> entry.Key.StartsWith("/lots/{id}/"))
            |> Seq.collect (fun entry -> entry.Value.Operations.Values)
            |> Seq.map (fun operation -> operation.OperationId)
            |> Set.ofSeq

        let actualActions =
            actions.EnumerateObject() |> Seq.map (fun action -> action.Name) |> Set.ofSeq

        Assert.Equal<Set<string>>(expectedActions, actualActions)

    [<Theory>]
    [<MemberData(nameof LotTransitionMatrixTests.Cases)>]
    [<Trait("Category", "Integration")>]
    [<Trait("Category", "Param")>]
    member _.``XR-LOT-001 状態と操作の全組合せおよび競合時の不変性``(state: string, action: string, stale: bool) = task {
        fixture.Reset()
        use client = fixture.NewClient()
        let sequence = Interlocked.Increment(&nextSequence)

        let! created =
            postJson
                client
                "/lots"
                (createLotBody
                    { emptyLotOverrides with
                        Location = Some(JString "XR")
                        Seq = Some(JInt sequence) })

        Assert.Equal(HttpStatusCode.OK, created.StatusCode)
        let! createdBody = readBody created
        let lotId = (parseJson createdBody).GetProperty("lotNumber").GetString()
        let definition = states.GetProperty state
        let mutable version = 1

        // version=0の入力検証と競合検査を分離する。正の古いversionを用意する。
        if stale && state = "manufacturing" then
            for setup in [ "completeManufacturing"; "cancelManufacturingCompletion" ] do
                let! response = sendAction client lotId setup version
                Assert.Equal(HttpStatusCode.OK, response.StatusCode)
                version <- version + 1

        for setup in definition.GetProperty("setup").EnumerateArray() do
            let! response = sendAction client lotId (setup.GetString()) version
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            version <- version + 1

        let! beforeResponse = getReq client ("/lots/" + lotId)
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode)
        let! beforeBody = readBody beforeResponse
        let before = parseJson beforeBody
        Assert.Equal(state, before.GetProperty("status").GetString())
        let beforeDatabase = snapshot sequence lotId
        let allowed, target = definition.GetProperty("allowed").TryGetProperty action
        let! response = sendAction client lotId action (if stale then version - 1 else version)

        if stale || not allowed then
            let status, problem =
                if stale then
                    HttpStatusCode.Conflict, "optimistic-lock-conflict"
                else
                    HttpStatusCode.BadRequest, "invalid-state-transition"

            let! _ = assertProblemDetails problem status response
            let! afterResponse = getReq client ("/lots/" + lotId)
            Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode)
            let! afterBody = readBody afterResponse
            Assert.Equal(beforeBody, afterBody)
            Assert.Equal(beforeDatabase, snapshot sequence lotId)
        else
            Assert.Equal(HttpStatusCode.OK, response.StatusCode)
            let! afterResponse = getReq client ("/lots/" + lotId)
            Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode)
            let! afterBody = readBody afterResponse
            let after = parseJson afterBody
            Assert.Equal(target.GetString(), after.GetProperty("status").GetString())
            Assert.Equal(version + 1, after.GetProperty("version").GetInt32())

            for name in [ "lotNumber"; "division"; "department"; "section"; "details" ] do
                Assert.Equal(before.GetProperty(name).GetRawText(), after.GetProperty(name).GetRawText())
    }
