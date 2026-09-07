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

        verifier.WithHttpEndpoint(Uri(sprintf "http://127.0.0.1:%d" fixture.Port)).WithFileSource(pactFile).Verify()

/// 軽量実行では明示的に skip する。全量実行は Broker URL と成功した TRX を必須にする。
type BrokerFactAttribute() as this =
    inherit FactAttribute()

    do
        if String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PACT_BROKER_URL")) then
            this.Skip <- "Broker 検証は全量品質ゲートで実行する"

/// Broker から取得した契約を、状態を準備した実プロバイダへ再生する。
[<Collection("ApiAuthOff")>]
type PactBrokerProviderTests(fixture: AuthOffFixture) =
    do fixture.Reset()

    [<BrokerFact>]
    [<Trait("Category", "Pact")>]
    member _.``provider satisfies frontend pact via broker``() =
        let brokerUrl = Environment.GetEnvironmentVariable("PACT_BROKER_URL")
        Assert.False(String.IsNullOrWhiteSpace brokerUrl, "全量検証には Pact Broker が必要です")
        StateHandlers.setUpAll fixture
        let config = PactVerifierConfig()
        use verifier = new PactVerifier("sales-management", config)

        verifier
            .WithHttpEndpoint(Uri(sprintf "http://127.0.0.1:%d" fixture.Port))
            .WithPactBrokerSource(Uri(brokerUrl), fun opts -> opts.BasicAuthentication("pact", "pact") |> ignore)
            .Verify()
