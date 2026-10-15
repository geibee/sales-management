# OpenAPI Readers の脆弱性修正

## 目的と変更範囲

- 関連 Issue: https://github.com/geibee/sales-management/issues/41
- トラック C（依存更新、外から見える API の振る舞いは変更しない）。
- `Microsoft.OpenApi.Readers` を API・テストの両プロジェクトで 1.6.29 から 1.6.30 に更新する。
- [GHSA-7pxr-59rr-hqj2](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-7pxr-59rr-hqj2) の YAML エイリアス展開によるサービス拒否に対応する。同アドバイザリの 1.x 系修正版は 1.6.30。
- `TreatWarningsAsErrors` と NuGet の脆弱性監査を維持する。警告抑制・メジャーバージョン移行・API 契約変更は行わない。

## 検証

- 修正前の失敗: [nightly 38805955622](https://github.com/geibee/sales-management/actions/runs/38805955622) の NU1903。
- `ContractValidationTests` に既存 YAML 契約の characterization テストを追加。`/lots` の GET/POST、必須リクエスト、JSON メディア型、共有 400 応答と Problem Details プロパティが読み取れることを固定する。
- `openapi.yaml` は変更せず、repo 共通ゲートの oasdiff と既存の HTTP セマンティクス・応答契約テストで互換性を確認する。
- PR の verify に加え、`.ci/nightly-request` の既存起動経路で `ci.sh` 全量と backend-lifecycle E2E を実行する。プロジェクトファイルだけの変更では nightly の PR スコープに含まれないため、明示起動する。
- 実行結果・未解消の検査失敗は PR に記録する。
