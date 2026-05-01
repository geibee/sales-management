module SalesManagement.Tests.Pact.PactProviderTests

open System
open Xunit
open PactNet
open PactNet.Verifier

/// Pact プロバイダ検証テスト。Broker から Pact を pull し、ローカル起動した F# サービスに対して再生する。
/// 環境変数 PACT_BROKER_URL が設定された場合のみ実行する (CI 用)。
/// smoke 範囲では `dotnet build` が通ることのみを検証し、実テスト実行は ci.sh 側の docker-compose 起動後。
[<Fact>]
[<Trait("Category", "Pact")>]
let ``provider satisfies frontend pact`` () =
    let brokerUrl = Environment.GetEnvironmentVariable("PACT_BROKER_URL")

    let providerUrl =
        match Environment.GetEnvironmentVariable("PACT_PROVIDER_URL") with
        | null
        | "" -> "http://localhost:5000"
        | v -> v

    if String.IsNullOrEmpty(brokerUrl) then
        // PACT_BROKER_URL 未設定 → smoke 環境としてスキップ扱い (テスト pass)
        ()
    else
        let config = PactVerifierConfig()
        use verifier = new PactVerifier("sales-management", config)

        verifier
            .WithHttpEndpoint(Uri(providerUrl))
            .WithPactBrokerSource(
                Uri(brokerUrl),
                fun opts ->
                    opts
                        .BasicAuthentication("pact", "pact")
                        .PublishResults("0.0.0-local", fun b -> b.ProviderBranch("main") |> ignore)
                    |> ignore
            )
            .Verify()
