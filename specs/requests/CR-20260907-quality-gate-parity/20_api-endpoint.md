# 既存エラー応答の契約文書化

対象は既存35 operation。新規 endpoint、入力・成功応答・製品実装の変更はない。
期待値は現行の認可・HTTPセマンティクス・レート制限・ヘルス・外部価格テストに従う。

| 対象 | 文書化する既存応答 | 条件 |
|---|---|---|
| 公開API以外 | 401 / 403 | 未認証 / ロール不足。既存 ProblemDetails |
| レート制限対象 | 429 | 上限超過。Retry-After。F# は空body、Java は ProblemDetails のため、この表現差も明記する |
| requestBody を持つ operation | 413 / 415 | 既存上限超過 / 非対応メディア |
| health | 503 | 既存の status=DOWN 応答 |
| externalPriceCheck | 502 / 503 | 上流失敗・タイムアウト・遮断。既存 ProblemDetails |

## 品質ゲート化対応表

| ID | ゲート |
|---|---|
| QG-API-01 | AuthorizationMatrixTests / AuthenticationHttpContractTest に実応答スキーマ照合を接続 |
| QG-API-02 | HttpSemanticsTests / RateLimitAndCacheTests / SalesCaseHttpContractTest |
| QG-API-03 | ContractValidationTests / ContractValidationTest の共通反例 fixture |
| QG-API-04 | oasdiff / Spectral / Java・TypeScript の生成ドリフト検査 |

## 契約差分の承認

依頼者の回答待ち。互換な応答追加に限る。新規 endpoint・破壊的変更を検出した場合は対象外とし、実装を進めない。
