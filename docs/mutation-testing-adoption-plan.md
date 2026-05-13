# ミューテーションテスト導入計画

## 目的

ミューテーションテストを導入し、既存テストが「実装の誤りを本当に検出できるか」を測る。行カバレッジでは検出できない弱い assert、境界値不足、PBT の入力空間不足を見つけ、ドメイン層と validation 層のテスト品質を高める。

## 導入判断

このリポジトリには導入余地がある。特に F# のドメイン純粋関数は、ミューテーションテストの対象として適している。

ただし、Stryker.NET の F# 対応は存在するものの、公式ドキュメント上も C# と完全同等の成熟度とは読まない。初期導入では CI ゲートにせず、PoC と report-only から始める。

## 公式ドキュメント上の前提

- Stryker.NET は .NET 向けミューテーションテストツールで、NuGet / dotnet tool として導入する。
- Stryker.NET technical reference には F# が対象言語として記載されている。
- F# 用の実装として `FsharpCompilingProcess`, `FsharpMutationProcess`, `FSharp.Compiler.Service`, `FSharp.Core` などが説明されている。
- 一方で F# technical reference には、C# の全機能が F# に移植されているわけではないこと、`mutanthelpers injection` と auto generated code detection が未対応であることが記載されている。
- この repo の test project は複数 project reference を持つため、Stryker 実行時は mutation 対象 project を明示する。

## 既存検査との役割分担

| 手段 | 主な問い | この repo での役割 |
| --- | --- | --- |
| coverlet | どの行が実行されたか | 通常 CI の coverage 測定 |
| FsCheck | 性質が広い入力で保たれるか | ドメイン純粋関数の往復性・順序性・不変性 |
| Schemathesis | HTTP contract と実装が一致するか | OpenAPI 境界の fuzz |
| TLA+ | 並行・時間変化の仕様が安全か | 楽観ロック、Outbox、状態遷移の仕様検証 |
| Mutation testing | テストが意図的なバグを検出できるか | assert の強さと境界値テストの品質測定 |

## 対象範囲

### 初期対象

最初は `apps/api-fsharp/src/SalesManagement/SalesManagement.fsproj` のうち、次のファイルに限定する。

- `Domain/Types.fs`
- `Domain/SmartConstructors.fs`
- `Domain/Errors.fs`
- `Domain/LotWorkflows.fs`
- `Domain/SalesCaseWorkflows.fs`
- `Domain/ReservationCaseWorkflows.fs`
- `Domain/ConsignmentCaseWorkflows.fs`
- `Api/LotDtos.fs`

理由:

- 純粋関数が多く、反例が読みやすい。
- `>=` と `>` の取り違え、`Ok` / `Error` の条件漏れ、保持すべき値の欠落など、実害に近い mutant が出やすい。
- 既存の FsCheck / integration test と照合しやすい。

### 初期対象外

初期導入では次を対象外にする。

- `Infrastructure/*`
- `Program.fs`
- `Api/*Routes.fs`
- migration / DB 永続化
- 外部 API client
- batch runner / migrator / DevTokenMint
- frontend

理由:

- DB、HTTP、DI、外部 stub、Testcontainers の影響で実行時間と flaky risk が高い。
- equivalent mutant やノイズが増えやすい。
- 最初の目的はテスト品質の信号を得ることであり、全体網羅ではない。

## 配置方針

導入が安定した段階で、次のファイルを追加する。

```
apps/api-fsharp/
├── stryker-config.json
├── tools/
│   └── mutation/
│       ├── ci.sh
│       └── mutation-json-to-sarif.py
└── ci-results/
    └── sarif/
        └── mutation.sarif
```

`dotnet-stryker` は `apps/api-fsharp/dotnet-tools.json` に local tool として追加する。global install には依存しない。

## フェーズ計画

### Phase 0: 手動 PoC

目的は、現行の .NET 10 / F# / xUnit 構成で Stryker.NET が実行できるか確認すること。

試すコマンド案:

```bash
cd apps/api-fsharp/tests/SalesManagement.Tests
dotnet stryker --project ../../src/SalesManagement/SalesManagement.fsproj
```

ただし、初回は config で mutate 対象を `Domain/*.fs` の小さい範囲に絞る。

確認項目:

- Stryker.NET が F# project を parse できる。
- initial test run が通る。
- `Domain/Types.fs` または `Domain/SmartConstructors.fs` に mutant が生成される。
- HTML または JSON report が出る。
- xUnit / FsCheck test が mutation run 上でも実行される。

完了条件:

- 手動で 1 回以上 report を取得できる。
- 実行時間と失敗モードが記録されている。
- F# 対応上の制約があれば、導入継続可否を判断できる。

### Phase 1: Domain baseline

対象を Domain 層に限定して baseline score を取る。

対象:

- `Domain/Types.fs`
- `Domain/SmartConstructors.fs`
- `Domain/*Workflows.fs`

期待する mutant:

- `>=` / `>` / `<` / `<=` の境界変更
- `Ok` / `Error` 条件の変更
- `match` 分岐の変更
- record field の代入変更
- list / NonEmptyList 関連の条件変更

完了条件:

- baseline mutation score を記録する。
- survived mutant を分類する。
- すぐに直すべき test gap と、無視してよい equivalent / low-value mutant を分ける。

### Phase 2: Validation baseline

対象に `Api/LotDtos.fs` を追加する。

重点:

- `validateCreateLotRequest`
- `validateLotNumber`
- `validateDetails`
- `validateDetail`

期待する検出:

- `details` 空配列が許可される mutant
- `count >= 1` が緩む mutant
- `quantity >= 0.001` が緩む mutant
- `lotNumber.location` の空文字許可 mutant
- item category の不正値許可 mutant

完了条件:

- Schemathesis の OpenAPI schema 改善と同じ validation 制約を、F# test 側でも検出できる。
- survived mutant があれば、FsCheck または example-based test に戻して補強する。

### Phase 3: Report-only CI

`tools/mutation/ci.sh` を追加し、手動または nightly で実行する。

方針:

- 通常 `apps/api-fsharp/ci.sh` にはまだ組み込まない。
- `MUTATION_ENABLED=1` のような opt-in で起動する。
- 結果を `ci-results/mutation/` に保存する。
- JSON report を `ci-results/sarif/mutation.sarif` に変換する。
- `ci-results/merged.sarif` へ統合する場合も、最初は warning 扱いにする。

完了条件:

- CI 上で report が取得できる。
- 失敗しても通常 CI を壊さない。
- 実行時間が許容範囲に収まる。

### Phase 4: Threshold 導入

baseline が安定してから threshold を設定する。

初期案:

- `threshold-break`: baseline より少し低い値
- `threshold-low`: baseline
- `threshold-high`: baseline + 10

注意:

- 100% は目標にしない。
- equivalent mutant を追いかけない。
- business critical な Domain / validation mutant の survived を優先する。

完了条件:

- mutation score の低下を PR 上で検知できる。
- threshold が開発速度を過度に落とさない。

## 初期 `stryker-config.json` 案

最初の config は report-only とし、対象を狭くする。

```json
{
  "stryker-config": {
    "project": "src/SalesManagement/SalesManagement.fsproj",
    "reporters": ["progress", "html", "json"],
    "mutation-level": "Standard",
    "test-runner": "vstest",
    "mutate": [
      "Domain/Types.fs",
      "Domain/SmartConstructors.fs",
      "Domain/Errors.fs",
      "Domain/*Workflows.fs",
      "Api/LotDtos.fs",
      "!**/Program.fs",
      "!**/Infrastructure/**/*.fs",
      "!**/*Routes.fs"
    ],
    "thresholds": {
      "high": 80,
      "low": 60,
      "break": 0
    }
  }
}
```

実際の path 解決は、Stryker 実行ディレクトリで確認してから調整する。

## SARIF 連携方針

既存の CI 方針に合わせ、最終的には次へ出力する。

- tool 別: `apps/api-fsharp/ci-results/sarif/mutation.sarif`
- 統合: `apps/api-fsharp/ci-results/merged.sarif`

ruleId 案:

- `mutation.survived`
- `mutation.timeout`
- `mutation.no-coverage`
- `mutation.runtime-error`

level 案:

- `survived`: 初期は `warning`
- `timeout`: 初期は `warning`
- `no-coverage`: 初期は `note`
- `runtime-error`: `error` でもよいが、PoC 中は `warning`

## テスト補強方針

survived mutant が出た場合、次の順で対応する。

1. 仕様として本当に検出すべきか判断する。
2. 検出すべきなら、まず FsCheck property で表現できないか検討する。
3. 境界値が明確なら example-based test を追加する。
4. HTTP contract の問題なら Schemathesis / OpenAPI schema 側へ戻す。
5. equivalent mutant は ignore 対象として記録する。

## 成功基準

- F# project に対して Stryker.NET が実行できることを確認できる。
- Domain 層の baseline mutation score が取得できる。
- survived mutant から、実際に意味のある test gap を 3 件以上抽出できる。
- mutation report が CI 成果物として保存できる。
- 通常 CI の所要時間を大きく悪化させない。

## リスク

- Stryker.NET の F# 対応が、この repo の .NET 10 / F# 構文に追いついていない可能性がある。
- xUnit / FsCheck との組み合わせで coverage analysis が不安定になる可能性がある。
- integration tests を含めると実行時間が長すぎる可能性がある。
- equivalent mutant が多いと、改善タスクとして扱いにくい。
- F# の compile order や `Program.fs` 前提の制約に当たる可能性がある。

## 次の一手

最初の PR では実装変更を入れず、手動 PoC 用の `stryker-config.json` だけを追加する。その後、`Domain/SmartConstructors.fs` と `Domain/Types.fs` に限定して Stryker.NET を実行し、F# 対応と report の品質を確認する。
