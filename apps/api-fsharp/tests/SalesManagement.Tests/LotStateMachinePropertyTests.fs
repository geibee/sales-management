module SalesManagement.Tests.LotStateMachinePropertyTests

open System
open System.Net
open Xunit
open FsCheck
open FsCheck.FSharp
open FsCheck.Xunit
open SalesManagement.Domain.Types
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders
open SalesManagement.Tests.Support.Generators

// ── コマンド型 ──

type LotCommand =
    | CompleteManufacturing of DateOnly
    | CancelManufacturingCompletion
    | InstructShipping of DateOnly
    | CompleteShipping of DateOnly
    | InstructItemConversion of ConversionDestinationInfo
    | CancelItemConversionInstruction

// ── モデル状態 ──

type LotModelState =
    | MManufacturing
    | MManufactured
    | MShippingInstructed
    | MShipped
    | MConversionInstructed

// ── モデル遷移 ──

type LotTransitionError = InvalidTransition of fromStatus: string * command: string

let stepModel (command: LotCommand) (state: LotModelState) : Result<LotModelState, LotTransitionError> =
    match state, command with
    | MManufacturing, CompleteManufacturing _ -> Ok MManufactured
    | MManufactured, CancelManufacturingCompletion -> Ok MManufacturing
    | MManufactured, InstructShipping _ -> Ok MShippingInstructed
    | MShippingInstructed, CompleteShipping _ -> Ok MShipped
    | MManufactured, InstructItemConversion _ -> Ok MConversionInstructed
    | MConversionInstructed, CancelItemConversionInstruction -> Ok MManufactured
    | _ -> Error(InvalidTransition(sprintf "%A" state, sprintf "%A" command))

// ── HTTP adapter ──

let private formatDate (d: DateOnly) = d.ToString("yyyy-MM-dd")

let executeCommand (client: Net.Http.HttpClient) (lotId: string) (version: int) (command: LotCommand) =
    task {
        match command with
        | CompleteManufacturing date ->
            let body = sprintf """{"date": "%s", "version": %d}""" (formatDate date) version
            return! postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) body
        | CancelManufacturingCompletion ->
            let body = sprintf """{"version": %d}""" version
            return! postJson client (sprintf "/lots/%s/cancel-manufacturing-completion" lotId) body
        | InstructShipping date ->
            let body = sprintf """{"deadline": "%s", "version": %d}""" (formatDate date) version
            return! postJson client (sprintf "/lots/%s/instruct-shipping" lotId) body
        | CompleteShipping date ->
            let body = sprintf """{"date": "%s", "version": %d}""" (formatDate date) version
            return! postJson client (sprintf "/lots/%s/complete-shipping" lotId) body
        | InstructItemConversion info ->
            let body = sprintf """{"destinationItem": "%s", "version": %d}""" info.DestinationItem version
            return! postJson client (sprintf "/lots/%s/instruct-item-conversion" lotId) body
        | CancelItemConversionInstruction ->
            let body = sprintf """{"version": %d}""" version
            return! deleteWithBody client (sprintf "/lots/%s/instruct-item-conversion" lotId) (Some body)
    }

// ── レスポンス解釈 ──

let parseStatus (statusStr: string) : LotModelState =
    match statusStr with
    | "manufacturing" -> MManufacturing
    | "manufactured" -> MManufactured
    | "shipping_instructed" -> MShippingInstructed
    | "shipped" -> MShipped
    | "conversion_instructed" -> MConversionInstructed
    | s -> failwithf "unknown status: %s" s

let parseResponse (resp: Net.Http.HttpResponseMessage) =
    task {
        let! body = readBody resp
        let json = parseJson body
        let status = json.GetProperty("status").GetString()
        let version = json.GetProperty("version").GetInt32()
        return status, version
    }

// ── Generator ──

let private conversionInfoGen = gen {
    return { DestinationItem = "変換先品目" }
}

let lotCommandGen: Gen<LotCommand> =
    Gen.oneof [
        Domain.dateGen |> Gen.map CompleteManufacturing
        Gen.constant CancelManufacturingCompletion
        Domain.dateGen |> Gen.map InstructShipping
        Domain.dateGen |> Gen.map CompleteShipping
        conversionInfoGen |> Gen.map InstructItemConversion
        Gen.constant CancelItemConversionInstruction
    ]

let lotCommandListGen: Gen<LotCommand list> =
    Gen.choose (0, 20) |> Gen.bind (fun n -> Gen.listOfLength n lotCommandGen)

type StateMachineArbitraries =
    static member LotCommandList() : Arbitrary<LotCommand list> = Arb.fromGen lotCommandListGen
    static member LotCommon() = Domain.Arbitraries.LotCommon()
    static member DateOnly() = Domain.Arbitraries.DateOnly()

// ── ロット作成ヘルパー ──

let private createLot (client: Net.Http.HttpClient) =
    task {
        let r = Random()
        let year = 3000 + r.Next(0, 5000)
        let seq = r.Next(1, 9999)
        let lotId = sprintf "%d-T-%d" year seq

        let body =
            createLotBody
                { emptyLotOverrides with
                    Year = Some(JInt year)
                    Location = Some(JString "T")
                    Seq = Some(JInt seq) }

        let! resp = postJson client "/lots" body
        assert (resp.StatusCode = HttpStatusCode.OK)
        return lotId
    }

// ── Property ──

[<Collection("ApiAuthOff")>]
type LotStateMachinePropertyTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``単一コマンド: 製造中ロットに製造完了を指示すると200が返りstatusがmanufacturedになる``() =
        task {
            use client = fixture.NewClient()
            let! lotId = createLot client

            let! resp = executeCommand client lotId 1 (CompleteManufacturing(DateOnly(2026, 4, 22)))
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

            let! (status, version) = parseResponse resp
            Assert.Equal("manufactured", status)
            Assert.Equal(2, version)
        }

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``任意コマンド列に対してモデルとHTTPレスポンスの成功/失敗が一致する``() =
        task {
            use client = fixture.NewClient()

            let runCommands (commands: LotCommand list) =
                (task {
                    let! lotId = createLot client
                    let mutable modelState = MManufacturing
                    let mutable version = 1

                    for cmd in commands do
                        let modelResult = stepModel cmd modelState
                        let! resp = executeCommand client lotId version cmd

                        match modelResult with
                        | Ok nextState ->
                            if resp.StatusCode <> HttpStatusCode.OK then
                                failwithf "モデルはOkだがHTTPは%A。状態=%A, コマンド=%A" resp.StatusCode modelState cmd

                            let! (status, newVersion) = parseResponse resp
                            let actualState = parseStatus status

                            if actualState <> nextState then
                                failwithf "状態不一致。モデル=%A, 実際=%A, コマンド=%A" nextState actualState cmd

                            modelState <- nextState
                            version <- newVersion
                        | Error _ ->
                            if resp.StatusCode = HttpStatusCode.OK then
                                failwithf "モデルはErrorだがHTTPは200。状態=%A, コマンド=%A" modelState cmd
                })
                    .GetAwaiter()
                    .GetResult()

            let config = Config.QuickThrowOnFailure.WithMaxTest(30)
            let prop = Prop.forAll (Arb.fromGen lotCommandListGen) runCommands
            Check.One(config, prop)
        }

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``不正遷移後も状態が維持され、lotNumberは全遷移を通じて不変``() =
        task {
            use client = fixture.NewClient()

            let runCommands (commands: LotCommand list) =
                (task {
                    let! lotId = createLot client
                    let mutable modelState = MManufacturing
                    let mutable version = 1

                    for cmd in commands do
                        let modelResult = stepModel cmd modelState
                        let! resp = executeCommand client lotId version cmd

                        match modelResult with
                        | Ok nextState ->
                            if resp.StatusCode <> HttpStatusCode.OK then
                                failwithf "モデルはOkだがHTTPは%A。状態=%A, コマンド=%A" resp.StatusCode modelState cmd

                            let! (status, newVersion) = parseResponse resp
                            modelState <- parseStatus status
                            version <- newVersion
                        | Error _ ->
                            // 不正遷移後、GETで状態が変わっていないことを確認
                            let! getResp = getReq client (sprintf "/lots/%s" lotId)

                            if getResp.StatusCode <> HttpStatusCode.OK then
                                failwithf "GET失敗。lotId=%s" lotId

                            let! (getStatus, getVersion) = parseResponse getResp
                            let actualState = parseStatus getStatus

                            if actualState <> modelState then
                                failwithf "不正遷移後に状態が変化。期待=%A, 実際=%A, コマンド=%A" modelState actualState cmd

                            if getVersion <> version then
                                failwithf "不正遷移後にversionが変化。期待=%d, 実際=%d, コマンド=%A" version getVersion cmd

                    // 全コマンド実行後、lotNumberが初期値と一致することを確認
                    let! finalResp = getReq client (sprintf "/lots/%s" lotId)
                    let! finalBody = readBody finalResp
                    let finalJson = parseJson finalBody
                    let finalLotNumber = finalJson.GetProperty("lotNumber").GetString()

                    if finalLotNumber <> lotId then
                        failwithf "lotNumberが変化。期待=%s, 実際=%s" lotId finalLotNumber
                })
                    .GetAwaiter()
                    .GetResult()

            let config = Config.QuickThrowOnFailure.WithMaxTest(30)
            let prop = Prop.forAll (Arb.fromGen lotCommandListGen) runCommands
            Check.One(config, prop)
        }
