module SalesManagement.Tests.Pact.PactProviderTests

open System
open System.IO
open Xunit
open PactNet
open PactNet.Verifier
open SalesManagement.Tests.Support.ApiFixture
open SalesManagement.Tests.Pact

/// リポジトリルートの pacts/ ディレクトリ (bin/Debug/net10.0 から 7 階層上)
let private pactsDir =
    let baseDir = AppContext.BaseDirectory
    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "..", "pacts"))

/// Broker レスのローカル pact 検証。pacts/*.json を直接読み、Testcontainers で
/// 起動した実プロバイダに対して再生する。外部インフラ (Pact Broker) を一切
/// 要求しないため、通常の `dotnet test` = scripts/verify.sh のマージゲートとして
/// 常に実行される (Broker 不在で silent skip しない = fail-closed)。
[<Collection("ApiAuthOff")>]
type PactLocalProviderTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<Fact>]
    [<Trait("Category", "Integration")>]
    member _.``provider satisfies local pact files without broker``() =
        // provider state を事前に一括セットアップする (詳細は StateHandlers のコメント参照)
        StateHandlers.setUpAll fixture

        let pactFile = FileInfo(Path.Combine(pactsDir, "frontend-sales-management.json"))

        Assert.True(pactFile.Exists, sprintf "pact ファイルが見つかりません: %s" pactFile.FullName)

        let config = PactVerifierConfig()
        use verifier = new PactVerifier("sales-management", config)

        verifier
            .WithHttpEndpoint(Uri(sprintf "http://127.0.0.1:%d" fixture.Port))
            .WithFileSource(pactFile)
            .Verify()

/// Broker 経由の検証 (nightly / ci.sh 用)。PACT_BROKER_URL が設定された場合のみ
/// 実行し、検証結果を Broker へ publish する。マージゲートとしては上の
/// PactLocalProviderTests が常時実行されるため、こちらは「あれば使う」オプション。
[<Fact>]
[<Trait("Category", "Pact")>]
let ``provider satisfies frontend pact via broker`` () =
    let brokerUrl = Environment.GetEnvironmentVariable("PACT_BROKER_URL")

    let providerUrl =
        match Environment.GetEnvironmentVariable("PACT_PROVIDER_URL") with
        | null
        | "" -> "http://localhost:5000"
        | v -> v

    if String.IsNullOrEmpty(brokerUrl) then
        // PACT_BROKER_URL 未設定 → smoke 環境としてスキップ扱い (テスト pass)。
        // ローカル pact の検証は PactLocalProviderTests が常時カバーする。
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
