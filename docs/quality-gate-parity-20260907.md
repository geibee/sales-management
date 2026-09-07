# F# / Java 品質ゲート相互レビュー

F# を本流として、通常の push / pull request では F# の軽量ゲートだけを自動実行する。Java / Spring は `spring-nightly.yml` の `workflow_dispatch` から手動実行し、`full` を選んだ場合は軽量・重量・保証監査を一括で実行する。

実行経路は異なるが、保証する業務範囲は `quality/guarantees.json` に集約する。両実装で共通の応答 fixture、ロット状態遷移、認可拒否、値・操作列の property、migration、outbox、アーキテクチャ、Pact を同じ保証 ID で監査する。テスト件数やカバレッジの数値を同一にするのではなく、各保証に必要な最小ケース数と計測下限を言語ごとに定義する。

各実行は `ci-results/runs/<target>/<run-id>/` に隔離し、コマンドログ、テストレポート、SARIF、計測値、ハッシュを保存する。成功したコマンドでもテストレポートが欠落・古い・skip・失敗の場合は失敗とし、必須 SARIF の欠落・実行失敗・error finding も成功扱いにしない。`scripts/quality-evidence.py` と `scripts/assurance-audit.py` が実行単位の証跡を検査し、`merged.sarif` へ統合する。

重量ゲートには Gitleaks、Trivy、CycloneDX、Pact、ZAP、Schemathesis、実 API E2E、故障注入を含める。Java 固有の SpotBugs、PIT、CodeQL、F# / Spring parity も全量実行時の必須証拠として扱う。OpenAPI の既存レスポンス文書化は仕様承認待ちであり、承認前は実装・成功応答・入力契約を変更しない。
