# F#・Java テスト相互レビュー（2026-09-06）

## 目的と範囲

[品質ゲートの費用対効果レポート](quality-gate-cost-review-20260906.md)をテスト追加前のスナップショットとして保存した後、両実装のテストを比較した。
既存DSL・OpenAPI・実装で確認できる振る舞いを固定するトラックCの変更であり、製品コード・OpenAPI・業務ルールは変更していない。
共通の期待値は今回整理したcharacterizationデータであり、新たに顧客承認を受けた仕様を意味しない。

## 発見と追加検査

| 観点 | 相互レビューで確認した不足 | 追加した検査 |
|---|---|---|
| ロットの禁止遷移 | ランダムな操作列だけでは各状態と操作の組合せを毎回保証しない | 両言語で5状態×6操作の30ケースと、許可される6遷移の古いversionを検査。拒否・競合後のGET、DB行、明細、Outbox件数が不変であることも確認 |
| マトリクスの追随 | 手書きの状態・操作一覧は追加時に陳腐化する | 両言語で状態enumとOpenAPIの操作IDに共通表を照合 |
| F#の状態遷移PBT | モデルが失敗を予測すると、500を含むすべての非2xxを受理していた | テスト用HTTP判定を400・409に限定。認証失敗・リダイレクト・404・500・503を誤認しない回帰テスト |
| F#の予約納品 | 既存PBTでは共通項目・日付は確認するが、確定金額と元の予約価格情報の保持が不足 | 予約価格作成→確定→納品を通した金額・査定情報保持のPBT |
| JavaのドメインPBT | 値オブジェクト2 propertyが中心で、業務フローの値保持検査が不足 | ロット・直販・予約・委託の4 property。日付・金額・参照値の保持と取消による復元を各100試行で検査 |
| Javaの操作列 | use caseの禁止遷移・競合・イベントの組合せを生成していない | 実LotUseCasesに1〜40操作の列を100試行。状態・version・共通項目・イベントと、拒否時に保存しないことを検査 |
| Javaの存在しないロット | 全コマンド共通の保存・イベント不変条件を明示していない | 6コマンドすべてでNotFound、保存呼出し0、イベント0を検査 |
| Javaの認可 | F#は全操作のマトリクスがあるが、Javaは代表APIの例示中心 | OpenAPI生成インターフェースから保護対象を列挙。匿名401、ロールなし403、viewerの更新403とProblemDetailsを検査 |

## テストと仕様の対応

| ID | 対象 | ファイル |
|---|---|---|
| XR-LOT-000 / XR-LOT-001 | 共通ロット遷移表の完全性・成功・禁止・競合 | [共通JSON](../tests/fixtures/lot-transitions.json)、[F# HTTP](../apps/api-fsharp/tests/SalesManagement.Tests/IntegrationTests/LotTransitionMatrixTests.fs)、[Java HTTP](../apps/api-spring/api/src/test/java/com/example/salesmanagement/api/SalesCaseHttpContractTest.java) |
| XR-LOT-002 / XR-PBT-007 | Java use caseの不存在・操作列・失敗後不変条件 | [LotUseCasesProperties.java](../apps/api-spring/application/src/test/java/com/example/salesmanagement/application/LotUseCasesProperties.java) |
| XR-PBT-001 / XR-PBT-002 | F# PBTの拒否応答判定 | [StateMachineResponseTests.fs](../apps/api-fsharp/tests/SalesManagement.Tests/StateMachineResponseTests.fs)、[HttpHelpers.fs](../apps/api-fsharp/tests/SalesManagement.Tests/Support/HttpHelpers.fs) |
| XR-PBT-003 | 予約確定値の保持（両言語） | [SalesCasePropertyTests.fs](../apps/api-fsharp/tests/SalesManagement.Tests/SalesCasePropertyTests.fs)、[WorkflowProperties.java](../apps/api-spring/domain/src/test/java/com/example/salesmanagement/domain/WorkflowProperties.java) |
| XR-PBT-004〜006 | Javaのロット・直販・委託の値保持と取消 | [WorkflowProperties.java](../apps/api-spring/domain/src/test/java/com/example/salesmanagement/domain/WorkflowProperties.java) |
| XR-AUTH-001 | Javaの保護対象操作に対する拒否 | [AuthenticationHttpContractTest.java](../apps/api-spring/api/src/test/java/com/example/salesmanagement/api/AuthenticationHttpContractTest.java) |

F#テストは既存のSupportハーネスを利用する。Java HTTPテストも既存のSpringコンテキスト・Testcontainers・HTTPヘルパーを利用する。
認可・状態遷移テストのリクエスト増加に対応し、当該Javaテストだけレート上限を引き上げた。レート制限の製品設定や専用テストは変更していない。
共通表の期待値を各実装に独立して照合するため、「両実装が同じ応答なら正しい」という比較にはしていない。

検査ケースの純増はF#が43件、Javaが134件。Javaの内訳は認可91件、共通マトリクス37件、domain PBT 4件、application 2件である。
今回のローカルSurefire計測では、追加ケースのメソッド時間合計は約3.85秒だった。コンパイル・コンテキスト起動等を含むCI増分の測定ではないが、既存fixtureを共有することで起動コストの追加を抑えている。

## Red / Greenの証拠

- F#の応答判定回帰テストは、旧ヘルパーで6件失敗・5件成功となり、500等を受理する欠陥を検出した。ヘルパー修正後に対象検査は成功した。
- Javaのロットworkflowで、出荷指示時に製造完了日を出荷期限日で上書きする一時変異を入れると、XR-PBT-004が失敗した。jqwikは日付の差を2020-01-01と2020-01-02まで縮小した。
- F#の予約確定で、確定金額に入力額ではなく予約時の金額を保存する一時変異を入れると、XR-PBT-003が失敗した。
- 一時変異は復元済み。製品コードの差分はない。これらは選択した変異に対する検出確認であり、PIT等による全変異のスコア測定ではない。

統合検証は今回の変更のみを反映した検証用チェックアウトで、`VERIFY_SCOPE=all bash scripts/verify.sh` を実行した（最終確認2026-09-07）。
作業開始前から未追跡で存在した `scripts/tests/python/test_assurance_audit.py` は変更せず、このチェックアウトには持ち込んでいない。
同ファイルはまだ存在しない `scripts/assurance-audit.py` を参照するため、元の作業ディレクトリ全体の成功を示す検証とは区別する。

- F#: 943件成功、失敗・スキップ0件。テスト実行1分34秒。line 89.25%、branch 51.77%、API実到達35/35。ビルド・書式・lintも成功。
- Spring: 最終202件成功、失敗・スキップ0件。line 72.73%。静的解析付きMaven verifyは再確認時1分9秒で成功。
- frontend: 45ファイル、326件成功、既存todo 3件。テスト43.76秒、line 94.44%、branch 81.81%。型・lint・契約・生成ドリフト・knip・カバレッジ・本番ビルドの各ゲート成功。
- smoke E2E: 4件成功、12.3秒。ホストではChromiumの`libnspr4.so`不足で起動できなかったため、このステップをCIと同じ`mcr.microsoft.com/playwright:v1.59.1-noble`で再実行した。コンテナ内のpnpmはローカル検証と同じ10.33.0に固定した。
- repo共通: gitleaks・shellcheck・actionlint・bats 42件・ruff・pytest 49件成功。OpenAPIは基準ブランチから変更なし。

Javaの追加クラスでJUnitとjqwikを混在させると、Surefire XMLの`tests`属性が1、実際の`testcase`が2となる集計不一致を検出した。
固定例をjqwikの`@Example`へ統一し、Springスコープを再実行して属性・要素数とも2であることを確認した。
全スコープの単一実行は前述のブラウザ環境不足で終了しているが、その後のSpring再検証とコンテナ内smoke成功を合わせ、対象ゲートをすべて確認した。

検証ログ（ローカル）: `/tmp/sales-cross-review-verify.log`、`/tmp/sales-cross-review-spring-final.log`、`/tmp/sales-cross-review-smoke.log`。

今回のF# branch値は、事前レポートで参照したnightlyの76.74%と異なる。計測条件を揃えた比較が必要なため、nightly値を根拠にそのまま下限を引き上げていない。

## 残る差と保証の限界

1. **version=0の契約不一致**: 有効な状態のロット遷移に0を渡すと、F#は409、Javaは400となった。OpenAPIはversionを1以上と定義している。F#の入力検証を契約に合わせる製品修正は今回の挙動不変の範囲に含めず、別途対応する。今回の競合テストは、正の古いversionを用意し、入力違反と競合を分けている。
2. **認可の追加範囲**: Javaの今回のマトリクスは拒否側を対象とする。許可ロールで全業務操作が成功することの網羅、ロールの組合せ、資源単位の権限は別の検査が必要。
3. **状態遷移の追加範囲**: 共通HTTPマトリクスはロットを対象とする。直販・予約・委託の全状態×全操作の共通HTTPマトリクスや、Javaの実APIに対する生成操作列のPBTまでは追加していない。
4. **生成と境界の限界**: PBTの100試行はすべての入力・操作列の網羅を意味しない。固定マトリクスと既存境界テストを併用する。日付の大小関係について新しい業務制約は導入していない。
5. **ゲート自体の改善**: OpenAPIレスポンス照合の厳密化、SpringのPact実プロバイダ再生、実行証拠と合意仕様IDの照合など、費用対効果レポートの改善案すべてを実装したものではない。
