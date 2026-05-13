# 仕様検査方法論

作成日: 2026-05-13

## 目的

本ドキュメントは、販売管理ドメインの自然言語要求を、最小 DSL、実行可能な検査、形式仕様、ユースケース例に分解して扱うための方法論を定義する。

一般的な業務システムでは、すべての仕様を単一の形式体系へ決定論的に書き下すことは現実的ではない。したがって、本リポジトリでは「完全な形式仕様」を最初から目指すのではなく、重要な要求を分類し、検査可能な仕様カーネルを育てる。

## 基本方針

- 自然言語要求は、まず atomic requirement に分解する。
- 重要な要求は、関数型ドメインモデリング用 DSL に落とし込む。
- DSL で表現しきれない要求は、性質に応じて property test、API contract、TLA+、Alloy、ユースケース例に分岐する。
- 各要求は、元要求、DSL 断片、正例、反例、検査手段、検査状態を追跡可能にする。
- verifier や型検査を通ることを、仕様が正しいこととは見なさない。仕様の正しさは、要求との対応、正例、反例、反証可能性で別途確認する。

## 背景となる知見

2025-2026 年の形式仕様支援研究では、単に LLM で形式仕様を生成するのではなく、次の方向が重視されている。

- 形式仕様が verifier を通っても、過剰制約または過少制約により、要求意図とずれることがある。
- 自然言語要求は、大きな仕様へ一括変換するより、atomic requirement へ分解し、要求単位で traceability を持たせるほうが修正しやすい。
- positive example と negative example は、仕様そのものの妥当性を検査する補助証拠として有効である。
- 操作の前提と結果は、まず型、状態、遷移、エラーで表す。型に吸収できない外部状態、永続化、副作用、時相性、運用裁量は assumption または postcondition として明示する。
- LLM は仕様を一発生成する役割より、候補仕様の分解、反例生成、局所修正、説明補助に使うほうが現実的である。

### 主に参考にする研究

| 研究 | 本リポジトリでの使いどころ |
| --- | --- |
| [Intent-aligned Formal Specification Synthesis via Traceable Refinement](https://arxiv.org/abs/2604.10392) | 自然言語要求を atomic requirement に分解し、要求ごとに traceability と局所修正単位を持たせる設計の参考にする。 |
| [Validating Formal Specifications with LLM-generated Test Cases](https://arxiv.org/abs/2510.23350) | DSL 仕様そのものを、正例/反例で検査する運用の参考にする。LLM は DSL 生成よりも、仕様 validation 用の例生成に使う。 |
| [Beyond Postconditions: Can Large Language Models infer Formal Contracts for Automatic Software Verification?](https://arxiv.org/abs/2510.12702) | `behavior` の前提と結果を、型で表せる部分と assumption/postcondition として補足すべき部分に分ける参考にする。 |
| [VeriAct: Beyond Verifiability -- Agentic Synthesis of Correct and Complete Formal Specifications](https://arxiv.org/abs/2604.00280) | verifier を通る仕様でも、要求意図に対して過剰制約または過少制約になり得るという注意点の根拠にする。 |

### 今回は主参考にしない研究

| 研究 | 今回の扱い |
| --- | --- |
| [ModelWisdom: An Integrated Toolkit for TLA+ Model Visualization, Digest and Repair](https://arxiv.org/abs/2602.12058) | TLA+ モデルを本格導入した後の反例理解、状態遷移グラフ可視化、修正支援には役立つ可能性がある。ただし、現時点の課題である要求分類、DSL 化、正例/反例による仕様 validation には直接効かないため保留する。 |
| [Neuro-Symbolic Generation and Validation of Memory-Aware Formal Function Specifications](https://arxiv.org/abs/2603.13414) | C のメモリ操作関数仕様が主対象で、本リポジトリの業務ドメイン DSL とは距離がある。LLM 生成仕様を反例と symbolic feedback で検査する一般パターンは参考になるが、その知見は VeriAct と LLM-generated test cases の論文で足りるため、主参考から外す。 |

## 要求の分類

自然言語要求は、次の分類を付与してから仕様化する。

| 分類 | 内容 | 主な表現先 | 主な検査 |
| --- | --- | --- | --- |
| 型 | 値、識別子、列挙、必須/任意、非空リスト | DSL `data`、各実装言語の型、OpenAPI schema | build、schema validation |
| 状態 | 販売案件、契約、ロットなどの状態 | DSL `data`、直和型または sealed hierarchy | property test、単体テスト |
| 遷移 | ある状態から別状態へ移る操作 | DSL `behavior`、純粋関数または副作用境界の内側の関数 | property test、integration test |
| 不変条件 | 常に破ってはいけない制約 | DSL コメント、property、TLA+/Alloy | property test、model check |
| 前提条件 | 操作が有効になる条件 | まず型/状態/入力で表す。型に乗らないものは assumption/guard | unit/property/API test |
| 事後条件 | 操作後に成立すべき条件 | まず戻り値/新状態/イベント型で表す。型に乗らないものは postcondition | unit/property test |
| 構造制約 | 関連、多重度、循環禁止、存在/非存在 | Alloy、DB constraint | Alloy、migration test |
| 時間/順序 | 期限、順序、eventual、並行性 | TLA+、state machine property | TLC、Coyote 相当、自前 PBT |
| 境界契約 | API 入出力、エラー形式、互換性 | OpenAPI、Pact | Schemathesis、contract test |
| 裁量/曖昧語 | 業務判断、例外運用、優先順位 | atomic requirement、正例/反例 | review、scenario test |

## 仕様化ワークフロー

### 1. Atomic requirement へ分解する

自然言語要求は、1つの検査可能な性質ごとに分割する。

悪い例:

```text
販売案件は審査後に契約でき、在庫が足りない場合はエラーにし、キャンセル時には引当を戻す。
```

よい例:

```text
R-SALES-001: 販売案件は appraised 状態のときだけ契約できる。
R-SALES-002: 契約時に必要数量を満たす在庫引当が存在しない場合、契約は失敗する。
R-SALES-003: 契約前の販売案件をキャンセルした場合、関連する引当は解放される。
```

### 2. 仕様カーネルへ落とす

DSL に落とせるものは、言語中立な最小の型と behavior にする。behavior の前提と結果は、原則として入力型、状態型、出力型、エラー型で表す。

```text
data SalesCaseStatus = Draft OR Appraised OR Contracted OR Cancelled

behavior CreateContract =
  AppraisedSalesCase AND NonEmptyList<InventoryReservation>
  -> SalesContract
  OR ContractCreationError
```

この例では、「販売案件は審査済みでなければ契約できない」という前提を `AppraisedSalesCase` に、「引当が1件以上必要である」という前提を `NonEmptyList<InventoryReservation>` に吸収している。F# では判別共用体や smart constructor、Kotlin では sealed interface や value class など、各実装言語に合わせた表現へ写像する。

この段階では、すべての業務例外や副作用を DSL に詰め込まない。DSL は「型、状態、遷移、主要なエラー」を固定する仕様カーネルとして扱う。

型で表せない前提や結果は、behavior の補足情報として明示する。

```yaml
behavior: CreateContract
encoded_by_types:
  preconditions:
    - AppraisedSalesCase
    - NonEmptyList<InventoryReservation>
  postconditions:
    - SalesContract
    - ContractCreationError
assumptions:
  - 引当は永続化時点で有効である
  - 楽観ロック競合がない
postconditions_not_encoded_by_types:
  - 契約作成イベントが outbox に記録される
  - 販売案件は Contracted として永続化される
```

### 3. 正例と反例を付ける

仕様カーネルだけでは、意図とずれた仕様も成立し得る。各 atomic requirement には、最低限の positive example と negative example を付ける。

```yaml
id: R-SALES-001
source: "販売案件は審査後に契約できる"
kind: transition
dsl:
  behavior: CreateContract
  encoded_by_types:
    - AppraisedSalesCase
positive_examples:
  - salesCase.status = Appraised の場合、契約作成に進める
negative_examples:
  - salesCase.status = Draft の場合、契約作成は失敗する
  - salesCase.status = Cancelled の場合、契約作成は失敗する
checks:
  - unit
  - property
status: dsl
```

### 4. 検査手段を割り当てる

各要求に、最も安い検査から割り当てる。

| 優先 | 検査手段 | 適用条件 |
| --- | --- | --- |
| 1 | 型/build | 不正状態を DSL と実装言語の型で除外できる |
| 2 | unit test | 代表例で十分に意図を固定できる |
| 3 | property test | 入力空間、境界値、状態遷移が広い。F# では FsCheck を使う |
| 4 | API contract / Schemathesis | OpenAPI 境界で破れる |
| 5 | Alloy | 構造制約、多重度、存在/非存在を確認したい |
| 6 | TLA+ | 並行性、順序、eventual、deadlock を扱う |
| 7 | review only | 業務裁量で、機械検査に落とす価値が低い |

### 5. 反例を仕様改善に戻す

テストやモデル検査で反例が出たら、反例を単なるバグ報告で終わらせず、要求に戻す。

- DSL が不足しているなら DSL を更新する。
- 前提条件が曖昧なら、型で表せる前提か、assumption/guard として明示すべき前提かを分類する。
- 仕様が過剰制約なら、型で絞りすぎていないか、negative example が正しいかを見直す。
- 仕様が過少制約なら、型、invariant、error、postcondition のどこで補うべきかを分類する。
- 実装だけが誤っているならテストを追加して実装を修正する。

## Traceability スキーマ

要求ごとに次の情報を持つ。最初は Markdown/YAML でよい。必要になったら JSON/YAML として機械処理する。

```yaml
id: R-XXX-000
title: "短い要求名"
source: "元の自然言語要求"
kind: type | state | transition | invariant | precondition | postcondition | structure | temporal | api | policy
risk: high | medium | low
dsl:
  data: []
  behavior: []
  encoded_by_types:
    preconditions: []
    postconditions: []
  assumptions: []
  postconditions_not_encoded_by_types: []
  note: ""
examples:
  positive: []
  negative: []
checks:
  - type: build | unit | property | api | alloy | tla | review
    target: ""
    status: planned | implemented | passing | failing | skipped
coverage:
  implementation: []
  tests: []
  specs: []
open_questions: []
status: informal | classified | dsl | tested | model_checked | deferred
```

## DSL で表すもの、表さないもの

### DSL で表す

- ドメイン型
- 識別子
- 状態
- 状態遷移
- 主要な入力/出力
- 主要なエラー
- 非空、任意、列挙、多重度などの静的制約
- 型で表せる前提条件
- 型で表せる成功結果と失敗結果

### DSL だけで表さない

- 複数 aggregate にまたがる eventual consistency
- 並行実行時の線形化可能性
- 時間制約や retry/backoff
- 外部 API 障害や補償処理
- 複雑な権限、監査、運用裁量
- 仕様の妥当性を支える正例/反例

これらは DSL の外に捨てるのではなく、DSL の behavior に紐づく assumption、postcondition、property test、scenario、TLA+、Alloy、API contract へ接続する。

## 拡張の方向性: 軽量 DSL と生成 IR

この節はマスタープランではなく、将来 DSL/IR を拡張する場合の設計指針である。現時点の `dsl/domain-model.md` を重くすることは前提にしない。

最大の制約は、業務記述を担当するビジネスコンサルタントが DSL/IR を使いこなせなくなることである。したがって、ビジネス側の入力は軽く保ち、検査やコード生成に必要な重い構造はツールまたは LLM が生成する。

ただし、LLM が生成した IR をそのまま信頼しない。ConCodeEval の知見が示すように、LLM は DSL や schema の細粒度制約を安定して満たせず、妥当性判定も機械的 validator の代替にはならない。したがって、IR 生成を採用する場合は、静的 parser、schema validator、semantic validator を同時に実装する。

| 層 | 主な利用者 | 役割 | 例 |
| --- | --- | --- | --- |
| 業務記述 | ビジネスコンサルタント | 日本語で業務概念、ルール、例外、ユースケースを書く | 業務説明、正例、反例 |
| 軽量 DSL | ビジネスコンサルタント + 開発者 | 型、状態、主要 behavior だけを固定する | `data`、`behavior`、`AND`、`OR`、`List<T>`、`?` |
| 生成 IR / 検査仕様 | ツール + 開発者 | traceability、assumption、postcondition、検査対象を機械処理する | YAML/JSON IR、property、OpenAPI、TLA+/Alloy |

コンサルタントが直接書く対象は、原則として業務記述と軽量 DSL までに限定する。`encoded_by_types`、`assumptions`、`postconditions_not_encoded_by_types`、`checks` などの重い構造は、LLM/パーサーが候補生成し、開発者がレビューする。

軽量 DSL は、次の程度の表現力に抑える。

```text
data 販売案件状態 = 下書き OR 審査済み OR 契約済み OR キャンセル済み

behavior 契約を作成する =
  審査済み販売案件 AND 1件以上の在庫引当
  -> 契約
  OR 契約作成エラー
```

DSL を豊かにする代わりに、業務側には正例と反例を追加してもらう。

```text
成立する例:
- 審査済み販売案件に在庫引当がある場合、契約を作成できる

成立しない例:
- 下書き販売案件は契約できない
- キャンセル済み販売案件は契約できない
- 在庫引当がない場合は契約できない
```

ツール側は、軽量 DSL と例から次の生成物を作る。`validated IR` になるまでは、コード生成やテスト生成の入力にしない。

```text
日本語の業務要求
  -> 軽量 DSL
  -> deterministic parser
  -> AST
  -> LLM による IR 補完候補
  -> IR schema validation
  -> semantic validation
  -> validated IR
  -> F# / Kotlin などの型
  -> workflow 実装候補
  -> unit/property/API test
  -> traceability
```

validator は最低限、次の3層を持つ。

| validator | 役割 |
| --- | --- |
| 構文 validator | 軽量 DSL と generated IR が grammar/schema に合うかを検査する。 |
| 型・参照 validator | `data`、`behavior`、入力型、出力型、エラー型、例の参照が解決できるかを検査する。 |
| semantic validator | `AND/OR`、非空リスト、optional、状態遷移、assumption、postcondition、正例/反例の整合性を検査する。 |

レビュー時にコンサルタントへ提示するのは、IR 全体ではなく、解釈の要約と差分にする。

```text
解釈:
- 「契約を作成する」は、販売案件が審査済みであることを前提にしています。
- 「在庫引当」は1件以上必要です。
- 下書き、契約済み、キャンセル済みの販売案件では契約できません。
```

この方向性を採用する場合も、LLM に全ソースコードを自由生成させない。契約層と検査層を先に固定し、LLM は候補生成と修正支援に使う。

```text
自然言語要求
  -> atomic requirement
  -> 軽量 DSL / generated IR 候補
  -> parser / validator による validated IR
  -> deterministic generator で型・契約層を生成
  -> LLM で workflow 実装候補とテスト候補を生成
  -> build / test / property test / API contract で選別・修正
```

導入判断は、次の条件を満たす場合に限る。

- 軽量 DSL の人間向け構文を増やさずに済む。
- generated IR を人間が常時手書きしなくてよい。
- 軽量 DSL parser、IR schema validator、semantic validator をセットで実装できる。
- code/test generation は validated IR だけを入力にする。
- コンサルタントのレビュー単位が、構文ではなく業務解釈と正例/反例になっている。
- 生成物は build、test、schema validation、property test のいずれかで検査できる。

## 本リポジトリでの適用方針

### 初期対象

まず次の領域を対象にする。

- 在庫ロット状態遷移
- 販売案件状態遷移
- 引当と委託
- 契約作成の型で表せる前提と、型に乗らない assumption/postcondition
- API schema と domain type の整合性

### 成果物

| 成果物 | 役割 |
| --- | --- |
| `dsl/domain-model.md` | 仕様カーネル |
| `docs/specification-validation-methodology.md` | 本方法論 |
| `docs/requirements-traceability.md` | atomic requirement と検査証拠の一覧。今後追加する |
| `apps/api-fsharp/tests/SalesManagement.Tests/` | unit/property/integration test |
| `apps/api-fsharp/openapi.yaml` | API 境界契約 |
| `specs/tla/` | 時間・順序・並行性仕様。必要になった段階で追加する |
| `specs/alloy/` | 構造制約仕様。必要になった段階で追加する |

### 完了条件

この方法論を適用した要求は、少なくとも次のいずれかを満たす。

- 型で不正状態を表現不能にしている。
- DSL の型または behavior に対応している。
- 型に乗らない前提と結果が、assumption または postcondition として明示されている。
- 正例と反例があり、unit/property/API test のいずれかで検査されている。
- TLA+ または Alloy の model に対応している。
- 機械検査しない理由が明示されている。

## 運用ルール

- 新しい重要要求を追加するときは、最初に分類と risk を付ける。
- high risk の要求は、正例と反例なしに実装しない。
- behavior の前提と結果は、まず型で表せるかを検討する。
- 型に乗らない外部状態、副作用、時相性、運用裁量は assumption または postcondition として明示する。
- DSL 変更は、対応するテストまたは検査計画と一緒に行う。
- LLM で生成した仕様は、verifier 通過だけで採用しない。
- 反例、失敗テスト、Schemathesis の failure は、要求の分類または DSL の改善候補として扱う。
- 仕様が曖昧な場合は、実装で吸収せず、atomic requirement の open question として残す。

## 次に検討する候補

- `docs/requirements-traceability.md` を追加し、既存 DSL から atomic requirement を起こす。
- `dsl/domain-model.md` の behavior ごとに、型で表せる前提/結果、assumption、postcondition、正例/反例を対応付ける。
- high risk な状態遷移から property test を追加する。F# 実装では FsCheck を使う。
- 並行性を含む要求を抽出し、TLA+ または state-machine property test の対象にする。
- 構造制約を抽出し、Alloy へ落とす価値があるものを分類する。
