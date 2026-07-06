module SalesManagement.Tests.Pact.StateHandlers

open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Support.HttpHelpers
open SalesManagement.Tests.Support.RequestBuilders

/// pact の provider state 名と、その状態を実 DB に作り込むセットアップ処理の対応表。
/// pacts/frontend-sales-management.json の `providerStates[].name` と一致させること。
///
/// Broker レス検証 (PactProviderTests) では検証開始前に全 state を適用する。
/// 現状の pact は全 interaction が同一 state を共有しており、GET は全状態を許容する
/// regex マッチャなので、事前一括セットアップで interaction の実行順に依存しない。
let private seedManufacturingLot (fixture: ApiFixture) : unit =
    // API 経由で正規に作成する (直接 INSERT だと行マッピングの前提が二重管理になる)。
    // createLotBody のデフォルト (year=2026, seq=1) + location=PACT → lotNumber "2026-PACT-001"
    use client = fixture.NewClient()

    let body =
        createLotBody
            { emptyLotOverrides with
                Location = Some(JString "PACT") }

    let resp = (postJson client "/lots" body).GetAwaiter().GetResult()

    if not resp.IsSuccessStatusCode then
        let respBody = (readBody resp).GetAwaiter().GetResult()

        failwithf
            "provider state のセットアップに失敗: POST /lots → %d %s"
            (int resp.StatusCode)
            respBody

/// state 名 → セットアップ関数。新しい interaction が state を追加したらここに登録する。
let stateHandlers: (string * (ApiFixture -> unit)) list =
    [ "lot 2026-PACT-001 が manufacturing 状態で存在する", seedManufacturingLot ]

/// 登録済みの全 provider state を適用する。
let setUpAll (fixture: ApiFixture) : unit =
    for _, handler in stateHandlers do
        handler fixture
