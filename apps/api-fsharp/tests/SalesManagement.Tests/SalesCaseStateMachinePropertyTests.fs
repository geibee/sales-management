module SalesManagement.Tests.SalesCaseStateMachinePropertyTests

/// 販売案件 3 サブタイプ (direct / reservation / consignment) のモデルベース
/// ステートフル PBT (issue #9 Tier2-10)。LotStateMachinePropertyTests と同じ設計:
///
///   1. 集約の状態遷移をモデル (純粋関数 stepModel) として書き下す
///   2. 任意コマンド列を生成し、モデルと実 API の成功/失敗・遷移先が一致するか検証
///   3. 不正遷移後に状態・version が変化していないこと (無副作用) を検証
///
/// DELETE 系エンドポイントは 204 (ボディなし) を返すため、各コマンド実行後の
/// 実状態は GET /sales-cases/{id} (SalesCaseDetailResponse) から観測する。
open System
open System.Net
open Xunit
open FsCheck
open FsCheck.FSharp
open SalesManagement.Tests.Support
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

// ── 共通: コマンド実行後の実状態の観測 ──

let private getCaseState (client: Net.Http.HttpClient) (caseId: string) = task {
    let! resp = getReq client (sprintf "/sales-cases/%s" caseId)

    if resp.StatusCode <> HttpStatusCode.OK then
        failwithf "GET /sales-cases/%s が %A" caseId resp.StatusCode

    let! body = readBody resp
    let json = parseJson body
    let status = json.GetProperty("status").GetString()
    let version = json.GetProperty("version").GetInt32()
    return status, version
}

let private isSuccess (code: HttpStatusCode) =
    code = HttpStatusCode.OK || code = HttpStatusCode.NoContent

// ── 共通: 製造完了ロット + 案件のセットアップ (public API 経由) ──

let private random = Random()

let private seedCase (client: Net.Http.HttpClient) (caseType: string) = task {
    // 他テスト・他イテレーションと衝突しないロット番号空間を使う
    let year = 8000 + random.Next(0, 1999)
    let seq = random.Next(1, 999)
    let location = sprintf "SM%d" (random.Next(0, 99))
    let lotId = sprintf "%d-%s-%03d" year location seq

    let lotBody =
        createLotBody
            { emptyLotOverrides with
                Year = Some(JInt year)
                Location = Some(JString location)
                Seq = Some(JInt seq) }

    let! createResp = postJson client "/lots" lotBody

    if createResp.StatusCode <> HttpStatusCode.OK then
        failwithf "seed: POST /lots が %A" createResp.StatusCode

    let! mfgResp =
        postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) """{"date":"2026-01-10","version":1}"""

    if mfgResp.StatusCode <> HttpStatusCode.OK then
        failwithf "seed: complete-manufacturing が %A" mfgResp.StatusCode

    let caseBody =
        createSalesCaseBody
            { emptySalesCaseOverrides with
                Lots = Some(JArray [ JString lotId ])
                CaseType = Some(JString caseType) }

    let! caseResp = postJson client "/sales-cases" caseBody

    if caseResp.StatusCode <> HttpStatusCode.OK then
        failwithf "seed: POST /sales-cases (%s) が %A" caseType caseResp.StatusCode

    let! body = readBody caseResp
    let json = parseJson body
    let caseId = json.GetProperty("salesCaseNumber").GetString()
    return caseId, lotId
}

// ── 共通: モデル vs 実 API の実行ループ ──

/// commands を順に実行し、モデルの成功/失敗・遷移先と実 API の観測が一致するか検証する。
/// 不正遷移 (モデル Error) では GET で状態・version が不変であることも検証する。
let private runAgainstModel
    (client: Net.Http.HttpClient)
    (caseId: string)
    (initialState: 'state)
    (stepModel: 'cmd -> 'state -> 'state option)
    (parseStatus: string -> 'state)
    (execute: string -> int -> 'cmd -> Threading.Tasks.Task<Net.Http.HttpResponseMessage>)
    (commands: 'cmd list)
    =
    (task {
        let mutable modelState = initialState
        let mutable version = 1

        for cmd in commands do
            let! resp = execute caseId version cmd
            let! (actualStatus, actualVersion) = getCaseState client caseId

            match stepModel cmd modelState with
            | Some nextState ->
                if not (isSuccess resp.StatusCode) then
                    failwithf "モデルは成功だが HTTP は %A。状態=%A, コマンド=%A" resp.StatusCode modelState cmd

                let actualState = parseStatus actualStatus

                if actualState <> nextState then
                    failwithf "状態不一致。モデル=%A, 実際=%A, コマンド=%A" nextState actualState cmd

                if actualVersion <> version + 1 then
                    failwithf "version 不一致。期待=%d, 実際=%d, コマンド=%A" (version + 1) actualVersion cmd

                modelState <- nextState
                version <- actualVersion
            | None ->
                if isSuccess resp.StatusCode then
                    failwithf "モデルは失敗だが HTTP は %A。状態=%A, コマンド=%A" resp.StatusCode modelState cmd

                let actualState = parseStatus actualStatus

                if actualState <> modelState then
                    failwithf "不正遷移後に状態が変化。期待=%A, 実際=%A, コマンド=%A" modelState actualState cmd

                if actualVersion <> version then
                    failwithf "不正遷移後に version が変化。期待=%d, 実際=%d, コマンド=%A" version actualVersion cmd
    })
        .GetAwaiter()
        .GetResult()

let private commandListGen (commandGen: Gen<'cmd>) : Gen<'cmd list> =
    Gen.choose (0, 12) |> Gen.bind (fun n -> Gen.listOfLength n commandGen)

let private pbtConfig = PbtConfig.standard 20

// ═══════════════════════════════════════════════ direct 案件

type DirectCommand =
    | CreateAppraisal
    | UpdateAppraisal
    | DeleteAppraisal
    | CreateContract
    | DeleteContract
    | InstructShipping
    | CancelShippingInstruction
    | CompleteShipping

type DirectState =
    | DBeforeAppraisal
    | DAppraised
    | DContracted
    | DShippingInstructed
    | DShippingCompleted

let stepDirect (cmd: DirectCommand) (state: DirectState) : DirectState option =
    match state, cmd with
    | DBeforeAppraisal, CreateAppraisal -> Some DAppraised
    | DAppraised, UpdateAppraisal -> Some DAppraised
    | DAppraised, DeleteAppraisal -> Some DBeforeAppraisal
    | DAppraised, CreateContract -> Some DContracted
    | DContracted, DeleteContract -> Some DAppraised
    | DContracted, InstructShipping -> Some DShippingInstructed
    | DShippingInstructed, CancelShippingInstruction -> Some DContracted
    | DShippingInstructed, CompleteShipping -> Some DShippingCompleted
    | _ -> None

let parseDirectStatus (s: string) : DirectState =
    match s with
    | "before_appraisal" -> DBeforeAppraisal
    | "appraised" -> DAppraised
    | "contracted" -> DContracted
    | "shipping_instructed" -> DShippingInstructed
    | "shipping_completed" -> DShippingCompleted
    | other -> failwithf "unknown direct status: %s" other

let private executeDirect
    (client: Net.Http.HttpClient)
    (lotId: string)
    (caseId: string)
    (version: int)
    (cmd: DirectCommand)
    =
    match cmd with
    | CreateAppraisal ->
        postJson client (sprintf "/sales-cases/%s/appraisals" caseId) (directAppraisalBody lotId version [] [])
    | UpdateAppraisal ->
        putJson client (sprintf "/sales-cases/%s/appraisals" caseId) (directAppraisalBody lotId version [] [])
    | DeleteAppraisal ->
        deleteWithBody client (sprintf "/sales-cases/%s/appraisals" caseId) (Some(versionOnlyBody (JInt version)))
    | CreateContract -> postJson client (sprintf "/sales-cases/%s/contracts" caseId) (directContractBody version [] [])
    | DeleteContract ->
        deleteWithBody client (sprintf "/sales-cases/%s/contracts" caseId) (Some(versionOnlyBody (JInt version)))
    | InstructShipping ->
        postJson
            client
            (sprintf "/sales-cases/%s/shipping-instruction" caseId)
            (dateVersionBody "date" (JString "2026-02-10") (JInt version))
    | CancelShippingInstruction ->
        deleteWithBody
            client
            (sprintf "/sales-cases/%s/shipping-instruction" caseId)
            (Some(versionOnlyBody (JInt version)))
    | CompleteShipping ->
        postJson
            client
            (sprintf "/sales-cases/%s/shipping-completion" caseId)
            (dateVersionBody "date" (JString "2026-02-20") (JInt version))

let private directCommandGen: Gen<DirectCommand> =
    Gen.elements
        [ CreateAppraisal
          UpdateAppraisal
          DeleteAppraisal
          CreateContract
          DeleteContract
          InstructShipping
          CancelShippingInstruction
          CompleteShipping ]

// ═══════════════════════════════════════════════ reservation 案件

type ReservationCommand =
    | CreateReservationAppraisal
    | Determine
    | CancelDetermination
    | Deliver

type ReservationState =
    | RBeforeReservation
    | RReserved
    | RConfirmed
    | RDelivered

let stepReservation (cmd: ReservationCommand) (state: ReservationState) : ReservationState option =
    match state, cmd with
    | RBeforeReservation, CreateReservationAppraisal -> Some RReserved
    | RReserved, Determine -> Some RConfirmed
    | RConfirmed, CancelDetermination -> Some RReserved
    | RConfirmed, Deliver -> Some RDelivered
    | _ -> None

let parseReservationStatus (s: string) : ReservationState =
    match s with
    | "before_reservation" -> RBeforeReservation
    | "reserved" -> RReserved
    | "reservation_confirmed" -> RConfirmed
    | "reservation_delivered" -> RDelivered
    | other -> failwithf "unknown reservation status: %s" other

let private executeReservation (client: Net.Http.HttpClient) (caseId: string) (version: int) (cmd: ReservationCommand) =
    match cmd with
    | CreateReservationAppraisal ->
        postJson client (sprintf "/sales-cases/%s/reservation/appraisals" caseId) (reservationAppraisalBody version)
    | Determine ->
        postJson client (sprintf "/sales-cases/%s/reservation/determine" caseId) (reservationDetermineBody version)
    | CancelDetermination ->
        deleteWithBody
            client
            (sprintf "/sales-cases/%s/reservation/determination" caseId)
            (Some(versionOnlyBody (JInt version)))
    | Deliver ->
        postJson client (sprintf "/sales-cases/%s/reservation/delivery" caseId) (reservationDeliveryBody version)

let private reservationCommandGen: Gen<ReservationCommand> =
    Gen.elements [ CreateReservationAppraisal; Determine; CancelDetermination; Deliver ]

// ═══════════════════════════════════════════════ consignment 案件

type ConsignmentCommand =
    | Designate
    | CancelDesignation
    | EnterResult

type ConsignmentState =
    | CBeforeConsignment
    | CDesignated
    | CResultEntered

let stepConsignment (cmd: ConsignmentCommand) (state: ConsignmentState) : ConsignmentState option =
    match state, cmd with
    | CBeforeConsignment, Designate -> Some CDesignated
    | CDesignated, CancelDesignation -> Some CBeforeConsignment
    | CDesignated, EnterResult -> Some CResultEntered
    | _ -> None

let parseConsignmentStatus (s: string) : ConsignmentState =
    match s with
    | "before_consignment" -> CBeforeConsignment
    | "consignment_designated" -> CDesignated
    | "consignment_result_entered" -> CResultEntered
    | other -> failwithf "unknown consignment status: %s" other

let private executeConsignment (client: Net.Http.HttpClient) (caseId: string) (version: int) (cmd: ConsignmentCommand) =
    match cmd with
    | Designate ->
        postJson client (sprintf "/sales-cases/%s/consignment/designate" caseId) (consignmentDesignateBody version)
    | CancelDesignation ->
        deleteWithBody
            client
            (sprintf "/sales-cases/%s/consignment/designation" caseId)
            (Some(versionOnlyBody (JInt version)))
    | EnterResult ->
        postJson client (sprintf "/sales-cases/%s/consignment/result" caseId) (consignmentResultBody version)

let private consignmentCommandGen: Gen<ConsignmentCommand> =
    Gen.elements [ Designate; CancelDesignation; EnterResult ]

// ═══════════════════════════════════════════════ Property

[<Collection("ApiAuthOff")>]
type SalesCaseStateMachinePropertyTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``direct: 任意コマンド列でモデルと実APIの遷移が一致し、不正遷移は無副作用``() =
        use client = fixture.NewClient()

        let run (commands: DirectCommand list) =
            let caseId, lotId = (seedCase client "direct").GetAwaiter().GetResult()

            runAgainstModel
                client
                caseId
                DBeforeAppraisal
                stepDirect
                parseDirectStatus
                (executeDirect client lotId)
                commands

        Check.One(pbtConfig, Prop.forAll (Arb.fromGen (commandListGen directCommandGen)) run)

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``reservation: 任意コマンド列でモデルと実APIの遷移が一致し、不正遷移は無副作用``() =
        use client = fixture.NewClient()

        let run (commands: ReservationCommand list) =
            let caseId, _ = (seedCase client "reservation").GetAwaiter().GetResult()

            runAgainstModel
                client
                caseId
                RBeforeReservation
                stepReservation
                parseReservationStatus
                (executeReservation client)
                commands

        Check.One(pbtConfig, Prop.forAll (Arb.fromGen (commandListGen reservationCommandGen)) run)

    [<Fact>]
    [<Trait("Category", "PBT")>]
    [<Trait("Category", "Integration")>]
    member _.``consignment: 任意コマンド列でモデルと実APIの遷移が一致し、不正遷移は無副作用``() =
        use client = fixture.NewClient()

        let run (commands: ConsignmentCommand list) =
            let caseId, _ = (seedCase client "consignment").GetAwaiter().GetResult()

            runAgainstModel
                client
                caseId
                CBeforeConsignment
                stepConsignment
                parseConsignmentStatus
                (executeConsignment client)
                commands

        Check.One(pbtConfig, Prop.forAll (Arb.fromGen (commandListGen consignmentCommandGen)) run)
