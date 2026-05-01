module SalesManagement.Tests.Pact.StateHandlers

open System.Collections.Generic

/// プロバイダ状態 (provider state) の登録ハンドラ。
/// PactNet 5.x の `IProviderStateMiddleware` または `WithProviderStateUrl` 経由で呼び出される想定。
/// 実際の DB seeding は本リポジトリの smoke 範囲外。state ハンドラ名 → no-op 関数を返す辞書を提供する。
let stateHandlers: IDictionary<string, System.Action> =
    let dict = Dictionary<string, System.Action>()

    dict.["lot-1234 が manufacturing 状態で存在する"] <-
        System.Action(fun () ->
            // TODO: テスト DB に lot-1234 (manufacturing) を INSERT する
            ())

    dict :> IDictionary<_, _>
