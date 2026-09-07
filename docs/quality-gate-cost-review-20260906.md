# 品質ゲートの費用対効果レビュー（2026-09-06）

## 評価の前提

- 目的: 顧客と合意した仕様を品質ゲートに落とし、その仕様を自動保証する。
- 対象: コミット `d79386ead9cb3c1a0c53797725b3bb4852bdcca3`。本レポートはテスト追加前の評価を保存する。
- 実行時間は GitHub Actions の実測、またはステージ間ログ時刻の概算。並列ジョブの時間は合算しない。平均・P95ではない。
- 保守工数、誤検知率、検出による損失回避額は未集計のため、費用対効果は検査内容と修正履歴に基づく定性的評価。
- テスト件数・カバレッジは言語間で粒度や計測対象が異なる。件数の大小だけでは品質を比較しない。
- 作業開始時から存在した未追跡の `scripts/tests/python/test_assurance_audit.py` は、導入済みゲートには数えない。

## 実測

| 実行 | 経過時間 | 内訳 |
|---|---:|---|
| [全スコープ verify（2026-08-19）](https://github.com/geibee/sales-management/actions/runs/32241410240) | 3分19秒 | 検査本体: F# 140秒、Spring 90秒、frontend 79秒、repo 共通4秒。並列 |
| [F# nightly（2026-09-05 UTC）](https://github.com/geibee/sales-management/actions/runs/33988607108) | 7分10秒 | 重量検査本体359秒、実API E2Eジョブ104秒。並列 |
| [Spring nightly（2026-08-19）](https://github.com/geibee/sales-management/actions/runs/32241452923) | 11分25秒 | 重量検査本体469秒、CodeQL解析52秒等。手動起動のみ |

repo 共通は検査4秒に対しツール導入約40秒。契約変更がなく oasdiff を使わない実行でも、その導入に25秒かかっていた。
F# nightly の Schemathesis はツール自己報告約47秒。ログの大量出力・バッファリングによりステージ境界との差がある。

## F# の評価

既存の業務保証は厚い。業務テストを削るより、重量CIの重複と参考情報生成を整理する。

| ゲート | コスパ | 推奨と保守上の理由 |
|---|---|---|
| コンパイル・警告エラー化 | 高 | 維持。型・分岐不整合を検出。保守負担が小さい |
| 業務ルール・境界値テスト | 非常に高 | 維持・仕様変更時に拡充。合意の直接的な検査 |
| FsCheck・状態遷移PBT | 高 | 維持。操作列、禁止遷移後の状態・version不変を検査。モデルの保守は必要 |
| DB統合・競合・マイグレーション | 高 | 維持。起動コストはあるがデータ破壊防止に寄与 |
| 認可マトリクス | 非常に高 | 維持。OpenAPIから操作を列挙でき、追随コストが小さい |
| APIレスポンスOpenAPI照合 | 非常に高 | 補修して維持。既存通信に相乗りできる |
| API操作カバレッジ | 高 | 維持。実2xx到達を記録するが業務分岐全体の保証ではない |
| DSLと実装の照合 | 中 | 補助として維持。関数の存在確認であり、型や意味の一致保証ではない |
| カバレッジラチェット | 中 | 分岐実測76.74%に対して下限50.6%。安定値を確認して基準を更新 |
| テスト件数ラチェット | 低 | 通常CIの下限0。仕様IDの検査漏れ検知への置換候補 |
| Fantomas・FSharpLint・アーキテクチャ検査 | 高 | 維持。nightlyでのアーキテクチャ再実行は全テストと重複 |
| ローカルPact | 中 | 実プロバイダへの再生は有用。ただし現在2 interaction |
| Schemathesis | 高 | 約47秒。実バグ検出実績あり。維持 |
| ZAP | 中 | 約114秒。基盤・ルール調整の保守負担があり、週次・関連変更・リリース前を候補にする |
| Gitleaks・Trivy | 高 | 約1秒／約8秒。維持。脆弱性DB更新の定期検査にも意味がある |
| SBOM | 中 | 約2秒。依存更新・リリース単位の成果物として維持 |
| scc | 低 | 計測は一瞬だが導入17秒。閾値判定なし。定期レポートへ移動 |
| Renovate dry-run | 低 | 約49秒、失敗を無視。合否判定と切り離す |

優先対応:

1. `Support/OpenApiValidation.fs` の未記載ステータスの黙認、`oneOf` の複数適合許容を補修する。
2. 合意仕様IDと今回成功したテストの照合を追加する。
3. カバレッジ基準を実態に追随させる。
4. Renovate dry-run約49秒、scc導入約17秒、重複アーキテクチャ検査約4秒を整理。観測実行で約70秒の削減余地。

Schemathesisは35操作中21操作を除外し、対象14操作でも5操作の404反復、6操作の生成入力大量拒否が警告されている。
一方、[修正コミット6f46e2d](https://github.com/geibee/sales-management/commit/6f46e2da2c71ce76817ed3976ea5addffa8d2462)に、null入力による500、不正JSON、負のoffsetによる500等の検出実績がある。削除せず、既存統合テストの契約照合と補完する。

## Java / Spring の評価

静的品質の基盤は充実している。追加ツールより、既存仕様の振る舞いを直接検証するテストへの投資を優先する。

| ゲート | コスパ | 推奨と保守上の理由 |
|---|---|---|
| javac・Error Prone | 高 | 維持。実装ミスをPRで広く検出 |
| NullAway | 高 | 維持。現在のNullMarkedはdomain・application。全層への適用と混同しない |
| Spotless・Checkstyle | 中〜高 | 維持しつつ重複書式規則をformatterへ寄せる |
| ArchUnit・ソース禁止規則 | 高 | 維持。独自正規表現より既存の構文・依存解析で表現できる規則を優先 |
| 業務ルール・境界値 | 非常に高 | 追加投資の最優先。禁止・境界・取消・失敗後不変を拡充 |
| jqwik | 現状の保証範囲は限定的 | 値オブジェクト2 propertyは有用。業務上重要な性質へ拡充 |
| HTTP・DB統合 | 高 | 維持・拡充。既にライフサイクル、競合、入力異常、Outbox等を検査 |
| 認証・認可 | 高 | 主に /lots の代表確認。全APIマトリクスへ拡充 |
| OpenAPI生成・dispatch整合 | 高 | 維持。ソース分岐の欠落検知と応答の正しさを区別する |
| 台帳・operation件数 | 低〜中 | 実行証拠へ置換・統合。ファイル存在や件数だけでは業務保証にならない |
| JaCoCo | 中 | 下限69.88%。維持し、重要業務の未検査分岐を確認 |
| 現行Pact処理 | 低 | JSON・ルート名確認とBroker登録取得。実プロバイダ検証へ変更 |
| F# / Springパリティ | 移行中は高 | 起動等込み約39秒。HTTPとDB比較は有用。ただし両者が同じ誤りなら通る |
| PIT | 高 | 約30秒。domain 49/101変異、application 5/51変異を検出。全体35.53%だけでなく未到達・生存変異を確認 |
| SpotBugs | 中 | 約42秒。Error Prone・NullAwayと重なる規則を整理 |
| CodeQL | 高 | 解析約52秒等。関連変更時・定期実行へ切り出す価値あり |
| Schemathesis | 中〜高 | 約58秒。維持。ただしF#と同じ状態遷移操作を除外 |
| ZAP | 中 | 約90秒。頻度見直し候補 |
| Gitleaks・Trivy | 高 | 合計約14秒。維持 |
| SBOM | 中 | 約7秒。依存更新・リリース単位に整理 |
| SARIF監査 | 高 | 短時間で結果欠落を検知。ただし弱い検査を入力しても保証は強くならない |

優先対応:

1. HTTPテストへの実レスポンス契約照合・到達記録。
2. 全APIの認可、禁止遷移・取消・失敗後の不変条件。
3. Pactを実プロバイダへ再生。独立デプロイ間の管理が必要な場合のみBrokerを運用。
4. PITの生存変異を使った重要業務テスト改善。
5. 必須保証を手動nightlyだけに置かず、関連変更時にも実行する。

## PBTの評価基準は両言語で同じ

評価差はFsCheckとjqwikの優劣ではなく、現在検査している範囲の差である。

| 観点 | F# | Java |
|---|---|---|
| 値の性質 | Smart Constructor境界、業務計算等 | 金額非負条件、ロット番号往復変換 |
| 状態遷移 | ロット・販売案件の操作列を生成 | jqwikによる状態遷移検査は未実装 |
| 実装との比較 | 実APIとモデルの状態を比較 | 値オブジェクト中心 |
| 禁止操作 | 状態・version不変も検査 | 主に通常の例示テスト |

Javaの2 propertyも安価で有用。「限定的」は低コスパを意味せず、保証範囲に追加余地があるという意味である。
言語間のPBT単独時間は未計測で、F#の方が速い・安いとは判断していない。
評価基準は、合意事項への直接性、例示で漏れる組み合わせの検出、モデル・生成器の保守費用で共通とする。
長期的には顧客承認済みの入力・期待値を共通化し、それぞれの実装を独立に検査することが望ましい。

## 共通・frontendゲート

| ゲート | コスパ | 推奨 |
|---|---|---|
| ShellCheck・actionlint・bats・ruff・pytest | 非常に高 | repo共通全体4秒。維持し、ツール導入を固定・キャッシュ |
| oasdiff | 高 | 維持。契約変更時のみ導入。契約差分の承認と互換性検査は別の責務 |
| Spectral・生成コードドリフト | 非常に高 | 合計約3秒。維持 |
| MSW契約照合 | 高 | 既存通信に相乗り。維持 |
| TypeScript・Biome・ast-grep・knip | 高 | 維持。重複規則だけ整理 |
| コンポーネント・a11y・PBT | 高 | テスト一式約46秒。表示・操作・入力エラーの仕様を主にここで保証 |
| カバレッジラチェット | 中 | 数値を目的にせず仕様網羅の補助として維持 |
| 本番ビルド・smoke E2E | 高 | 約11秒＋約6秒。維持 |
| 実API E2E | 代表導線なら高 | 検査約26秒、準備込み104秒。主要実装変更でも走るようスコープを改善 |

E2Eは現在開発サーバーを検査する。本番ビルド配信の確認への改善余地がある。
`retries: 0` と `trace: "on-first-retry"` の組合せでは失敗traceを取得できないため、失敗時保持へ変更すると調査費用を下げられる。
性能・負荷・配送遅延等は顧客が測定条件を合意した時点で追加し、k6やLighthouse等を一律には増設しない。

## 参照する実装

- [verify](../scripts/verify.sh)、[F# ci](../apps/api-fsharp/ci.sh)、[Spring ci](../apps/api-spring/ci.sh)
- [仕様運用](../specs/README.md)、[nightly scope](../scripts/lib/nightly-scope.sh)
- [F#契約照合](../apps/api-fsharp/tests/SalesManagement.Tests/Support/OpenApiValidation.fs)
- [Spring台帳検査](../apps/api-spring/scripts/verify-contracts.py)、[Springラチェット](../apps/api-spring/scripts/verify-quality-ratchets.py)
- [Spring Pact検査](../apps/api-spring/api/src/test/java/com/example/salesmanagement/api/BrokerlessPactContractTest.java)
- [OpenAPI 3.0.3](https://spec.openapis.org/oas/v3.0.3.html)、[Pactプロバイダ検証](https://docs.pact.io/implementation_guides/jvm/provider/junit5)、[PIT](https://pitest.org/)
