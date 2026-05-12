# Schemathesis 改善提案

## 目的

Schemathesis を「OpenAPI と HTTP 実装の契約検証」として安定運用する。現状は CI に統合済みだが、rate limit・認証設定・OpenAPI schema のズレにより、発見の多くが実装バグではなく検査環境由来の warning になっている。まず信号品質を上げ、その後に CI ゲートへ昇格する。

## 現状

- `apps/api-fsharp/ci.sh` で `schemathesis/schemathesis:stable` を実行している。
- `--checks all`, `-n 200`, `--seed 42`, `--request-timeout 2.0`, `--workers 1` により再現性は確保されている。
- `apps/api-fsharp/schemathesis-hooks.py` で多段階の事前状態を要求する operation を OpenAPI schema から除外している。
- JUnit XML を `scripts/junit-to-sarif.py` で SARIF に変換し、`ci-results/merged.sarif` に統合している。
- 直近の結果では、Schemathesis は 11 operation を対象に `warning=19`, `note=11` を出している。

## 主な課題

1. rate limit が fuzz を阻害している

   既定の `RateLimit:PermitLimit=100` に対して Schemathesis は `-n 200` で多数のリクエストを投げるため、429 が発生しやすい。これにより、schema-compliant request が 429 で拒否されたように見え、契約検証の信号が濁る。

2. 認証契約と検査環境が一致していない

   `openapi.yaml` は global `bearerAuth` を宣言している一方、通常のローカル起動では `Authentication:Enabled=false` になっている。このため、Schemathesis は `Authorization` header を必須と解釈するが、実装側は認証 OFF として動作する。

3. request schema の制約が実装より緩い

   `POST /lots` は実装では lotNumber、details 1 件以上、正の count、quantity 下限などを要求するが、OpenAPI schema 側では `required`, `minItems`, `minimum`, enum が不足している。結果として、実装が正しく reject する `{}` や空配列が schema-compliant と判定される。

4. stateless fuzz と状態依存 API が混在している

   `POST /sales-cases` は API 上は単独作成に見えるが、実装上は既存の manufactured lot が必要である。現状 hook では多段遷移 operation は除外しているが、作成 API の事前データ要件までは吸収できていない。

5. `No examples in schema` が残っている

   主要 operation に examples がないため、Schemathesis の発見再現性と、人間が読む契約仕様の品質が弱い。

## 改善方針

- Schemathesis はドメイン状態遷移の正しさではなく、HTTP 境界と OpenAPI 契約の検証に責務を絞る。
- false positive を削ってから SARIF severity を上げる。
- stateless API fuzz と stateful workflow fuzz を分ける。
- schema は実装に合わせるだけでなく、実装が守るべき公開契約として明示する。

## 提案タスク

### S1: Schemathesis 専用実行プロファイルを作る

`ci.sh` の Schemathesis 起動時に、fuzz 用設定を明示的に渡す。

- `--RateLimit:PermitLimit=100000` または `SCHEMATHESIS_RATE_LIMIT_PERMITS` を追加する。
- `--RateLimit:WindowSeconds=60` は維持する。
- 認証方針を `Authentication:Enabled=false` に固定するか、認証 ON + token hook に切り替える。
- 429 を OpenAPI に足すだけで済ませない。fuzz 中の 429 は検査ノイズなので、原則として発生させない。

完了条件:

- Schemathesis の warning から 429 起因の failure が消える。
- `GET /health` 以外でも `Too Many Requests` が再現コマンドに出ない。

### S2: 認証モードと OpenAPI security を揃える

短期案:

- Schemathesis は認証 OFF の contract として実行する。
- hook で raw schema の global `security` を取り除く、または fuzz 専用 OpenAPI を生成する。

中期案:

- `tools/DevTokenMint` で operator/viewer token を生成する。
- Schemathesis hook で operation ごとに `Authorization: Bearer <token>` を付与する。
- viewer API と operator API を分け、role 不足時の 403 も契約として検証する。

完了条件:

- `Missing Authorization` 系の false positive が消える。
- 認証 ON で検査する場合は、401/403 が意図した negative case としてだけ出る。

### S3: OpenAPI request schema を実装の validation に合わせる

優先対象:

- `POST /lots`
- `POST /sales-cases`
- list 系 query parameter
- `GET /api/external/price-check`

追加すべき制約:

- object の `required`
- `additionalProperties: false`
- array の `minItems`
- integer / number の `minimum`
- enum
- date format
- 代表的な `example`

完了条件:

- schema-compliant な最小例が実装で 2xx または仕様上許容した status になる。
- schema-violating な欠落・空配列・負数が 400 系になる。
- `No examples in schema` が主要 operation から消える。

### S4: `POST /sales-cases` の扱いを明確にする

選択肢は 2 つ。

1. stateless fuzz から除外する

   `POST /sales-cases` は既存 manufactured lot が必要なため、`schemathesis-hooks.py` の除外対象に追加する。

2. 事前データ hook を作る

   Schemathesis 実行前または case 生成時に、manufactured lot を DB に投入し、その lot number を `lots` に使う。

推奨は 2。作成 API は重要な入口なので、長期的には fuzz 対象に残す価値が高い。

完了条件:

- `POST /sales-cases` の schema-compliant request が事前状態不足で false positive にならない。

### S5: SARIF severity 昇格ルールを定義する

現状は `junit-to-sarif.py` で failure / error を warning に変換している。信号品質が上がるまでは妥当だが、昇格条件を明文化する。

提案:

- 429・認証不整合・schema 不足が解消されるまでは warning。
- `status_code_conformance` と `response_schema_conformance` は、2 週間安定後に error へ昇格する。
- `No examples in schema` は note のまま。ただし主要 operation ではゼロを目標にする。

完了条件:

- `merged.sarif` の Schemathesis run に error が出た場合、CI が落ちる。
- warning は改善バックログとして残すが、PR ブロックには使わない。

## 優先順位

1. S1: rate limit 無効化
2. S2: 認証契約の整合
3. S3: `POST /lots` schema 強化
4. S4: `POST /sales-cases` の事前データ対応
5. S5: SARIF severity 昇格

## 成果指標

- Schemathesis warning のうち 429 起因が 0 件。
- `Missing Authorization` 起因の false positive が 0 件。
- 主要 operation の `No examples in schema` が 0 件。
- `POST /lots` の valid / invalid 判定が OpenAPI と実装で一致する。
- SARIF に残る Schemathesis warning が、実装・契約どちらかの修正タスクへ直接変換できる。

## リスク

- OpenAPI schema を厳密化すると、frontend の generated contract に影響する。
- 認証 ON fuzz に切り替える場合、token 生成と role 切り替えの保守が必要になる。
- 事前データ hook は DB 状態に依存するため、seed 固定だけでは再現性が足りない可能性がある。

## 次の一手

最初の PR では `ci.sh` に Schemathesis 専用の rate limit override を追加し、429 起因の warning を消す。その結果を確認してから、OpenAPI schema の厳密化に進む。
