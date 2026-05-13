# Stateful PBT 実装計画

作成日: 2026-05-13
改訂日: 2026-05-13（API層対象に方針変更）

## 位置づけ

本ドキュメントは [`docs/pbt-fscheck-improvement-proposal.md`](./pbt-fscheck-improvement-proposal.md) の F5「状態機械 PBT」を具体化する実装計画である。

既存の FsCheck 改善提案は、generator 集約、境界値 property、DTO/domain 変換 property を含む全体提案である。本計画はその中から stateful PBT だけを切り出し、ロット状態遷移を最初の対象として、Red / Green / Refactoring で実装するための作業単位を定義する。

## 目的

現在の `LotPropertyTests.fs` は、決め打ちの遷移列に対する往復性、順序性、不変性を検査している。これは有効だが、任意のコマンド列に対して、モデルと実装が同じ状態に到達するかは検査していない。

stateful PBT では、コマンド列を生成し、抽象モデルと実装対象（API層）を同期させながら検査する。

## なぜ API 層を対象にするか

pure domain の workflow は型安全である:

```fsharp
completeManufacturing : DateOnly -> ManufacturingLot -> ManufacturedLot
```

不正遷移（例: `ManufacturedLot` を `completeManufacturing` に渡す）はコンパイルエラーになる。型が保証している制約を PBT で再検査する価値はない。

一方、API 層の transition 関数は `InventoryLot`（判別共用体）を受け取る:

```fsharp
let private completeManufacturingTransition (date: DateOnly) (lot: InventoryLot) : Result<InventoryLot, DomainError> =
    match lot with
    | Manufacturing m -> Ok(Manufactured(completeManufacturing date m))
    | _ -> Error(InvalidStateTransition "Lot is not in manufacturing state")
```

ここでは pattern match の書き間違いがコンパイルを通る。HTTP エンドポイント経由で検査することで、routing → validation → transition → persistence の全層を通した正しさを確認できる。

## 検査したい性質

- 許可された遷移は HTTP 200 を返し、レスポンスの status がモデルの次状態と一致する。
- 不正な遷移は HTTP 409 を返し、ロットの状態は変わらない。
- どの遷移列でもレスポンスの LotCommon 相当フィールドが初期値から変わらない。
- `Shipped` 到達後は全操作が 409 になる（終端状態）。
- version 不一致は 409（楽観ロック）。
- 反例はコマンド列として読める。

## 対象範囲

### 初期対象

HTTP エンドポイント経由のロット状態遷移。

対象エンドポイント:

- `POST /lots/{id}/complete-manufacturing`
- `POST /lots/{id}/cancel-manufacturing-completion`
- `POST /lots/{id}/instruct-shipping`
- `POST /lots/{id}/complete-shipping`
- `POST /lots/{id}/instruct-item-conversion`
- `DELETE /lots/{id}/instruct-item-conversion`

対象状態:

- `manufacturing`
- `manufactured`
- `shipping_instructed`
- `shipped`
- `conversion_instructed`

対象ファイル:

- `apps/api-fsharp/src/SalesManagement/Api/LotRoutes.fs`（検査対象の production code）
- `apps/api-fsharp/tests/SalesManagement.Tests/LotStateMachinePropertyTests.fs`（PBT）
- `apps/api-fsharp/tests/SalesManagement.Tests/Support/Generators.fs`

### 初期対象外

- 複数ロットをまたぐ在庫引当
- 並行実行や線形化可能性
- Schemathesis の stateful testing
- 販売案件の状態遷移（ロット安定後に検討）

## 基本設計

### 実装方式

`ApiFixture`（TestContainers + WebApplicationFactory）を使い、HTTP 経由でロットを操作する。FsCheck の `Gen<LotCommand list>` でコマンド列を生成し、各コマンドを対応する HTTP リクエストに変換して実行する。

### 1. モデルと実装状態を分離する

モデルは最小限の抽象状態にする。

```fsharp
type LotModelState =
    | MManufacturing
    | MManufactured
    | MShippingInstructed
    | MShipped
    | MConversionInstructed
```

実装状態は HTTP レスポンスの `status` フィールドから取得する。

### 2. コマンドを明示する

```fsharp
type LotCommand =
    | CompleteManufacturing of DateOnly
    | CancelManufacturingCompletion
    | InstructShipping of DateOnly
    | CompleteShipping of DateOnly
    | InstructItemConversion of ConversionDestinationInfo
    | CancelItemConversionInstruction
```

### 3. コマンドを HTTP リクエストに変換する

```fsharp
let executeCommand (client: HttpClient) (lotId: string) (version: int) (command: LotCommand) : Task<HttpResponseMessage> =
    match command with
    | CompleteManufacturing date ->
        postJson client (sprintf "/lots/%s/complete-manufacturing" lotId) (dateVersionBody date version)
    | InstructShipping date ->
        postJson client (sprintf "/lots/%s/instruct-shipping" lotId) (deadlineVersionBody date version)
    | ...
```

### 4. モデル遷移を小さく保つ

```fsharp
let stepModel command state =
    match state, command with
    | MManufacturing, CompleteManufacturing _ -> Ok MManufactured
    | MManufactured, CancelManufacturingCompletion -> Ok MManufacturing
    | MManufactured, InstructShipping _ -> Ok MShippingInstructed
    | MShippingInstructed, CompleteShipping _ -> Ok MShipped
    | MManufactured, InstructItemConversion _ -> Ok MConversionInstructed
    | MConversionInstructed, CancelItemConversionInstruction -> Ok MManufactured
    | _ -> Error InvalidModelTransition
```

### 5. レスポンスからモデル状態を抽出する

HTTP 200 レスポンスの JSON から `status` フィールドを読み、`LotModelState` に変換する。

```fsharp
let parseStatus (json: JsonNode) : LotModelState =
    match json["status"].GetValue<string>() with
    | "manufacturing" -> MManufacturing
    | "manufactured" -> MManufactured
    | "shipping_instructed" -> MShippingInstructed
    | "shipped" -> MShipped
    | "conversion_instructed" -> MConversionInstructed
    | s -> failwithf "unknown status: %s" s
```

## 実装ステップ

### S1: Support generator を先に整える ✅

完了。`LotPropertyTests.fs` の generator を `Support/Generators.fs` に集約した。

### S2: コマンド型と generator を追加する ✅

完了。`LotCommand` 型と `Gen<LotCommand list>` を `LotStateMachinePropertyTests.fs` に定義した。

### S3: HTTP 呼び出し adapter を実装する

目的: コマンドを HTTP リクエストに変換し、レスポンスを解釈できるようにする。

作業:

- `LotStateMachinePropertyTests.fs` から S3 旧版の `applyCommand`（pure domain adapter）を削除する。
- `executeCommand : HttpClient -> string -> int -> LotCommand -> Task<HttpResponseMessage>` を実装する。
- レスポンスから status と version を抽出する helper を実装する。
- テストクラスを `ApiFixture` 利用に変更する。

完了条件:

- 単一コマンド（`CompleteManufacturing`）を HTTP 経由で実行し、200 が返ることを手動確認する property がある。

### S4: sequential stateful property を追加する

目的: 任意コマンド列に対して、モデルと HTTP レスポンスの成功/失敗が一致することを検査する。

property:

```text
任意の初期 LotCommon と任意のコマンド列について、
POST /lots でロットを作成し（manufacturing 状態）、
順にコマンドを HTTP で実行したとき、
モデル遷移と HTTP レスポンスの成功(200)/失敗(409)が一致し、
成功時のレスポンス status がモデルの次状態と一致する。
```

完了条件:

- `Category=PBT` に含まれる。
- 失敗時に seed と shrink 後のコマンド列が読める。

### S5: 不正遷移後も状態維持 + 横断的性質を追加する

目的: 不正遷移後にロットの状態が変わらないこと、LotCommon が維持されることを検査する。

property:

```text
任意のコマンド列について、
不正遷移（409）の後に GET /lots/{id} で取得した status が直前と同じであり、
全遷移を通じて LotCommon 相当フィールドが初期値と一致する。
```

完了条件:

- 不正遷移を含むコマンド列が十分に生成される。
- 409 後の状態維持が GET で検証される。
- LotCommon の不変性が全ステップで検査される。

## テストファイル構成

```text
apps/api-fsharp/tests/SalesManagement.Tests/
├── LotPropertyTests.fs                  # 既存 pure domain PBT（残す）
├── LotStateMachinePropertyTests.fs      # API 層 stateful PBT
└── Support/
    └── Generators.fs                    # Domain generator 集約済み
```

## FsCheck 設定

- HTTP + DB を伴うため、`MaxTest` は少なめ（初期 20〜50）にする。
- コマンド列長は 0〜20。
- 失敗時のコマンド列可読性を優先する。
- CI では PBT 専用ジョブで実行時間を管理する。

## TDD の進め方

1. 探索: 既存 `StateTransitionParamTests` と `LotLifecycleTests` のパターンを確認する。
2. Red: 最小の HTTP stateful property（1コマンド）を追加する。
3. Green: `executeCommand` と status 抽出を実装する。
4. Refactoring: コマンド列対応に拡張する。
5. Red: 不正遷移後の状態維持 property を追加する。
6. Green: GET による状態確認を追加する。
7. Refactoring: LotCommon 不変性を横断的に検査する。

## リスクと対策

| リスク | 対策 |
| --- | --- |
| DB 付きテストが遅い | MaxTest を絞る。コマンド列長を制限する。PBT 専用ジョブに分離する。 |
| テスト間の状態干渉 | 各 property 実行ごとに新しいロットを作成する（ロット番号をランダム化）。 |
| version 管理の複雑さ | 成功時にレスポンスから version を取得し、次コマンドに渡す。失敗時は version 不変。 |
| 反例が読みにくい | `LotCommand` の表示を業務操作名にし、コマンド列長を制限する。 |
| shrink で意味の薄い反例になる | 日付や変換先情報は単純な generator にし、コマンド順序の shrink を優先する。 |

## 完了条件

- `LotStateMachinePropertyTests.fs` が API 層を HTTP 経由で検査している。
- `Category=PBT` で stateful PBT が実行できる。
- 任意コマンド列に対して、モデルと HTTP レスポンスの成功/失敗が一致する。
- 成功遷移では、レスポンスの status がモデルの次状態と一致する。
- 不正遷移では、409 が返り状態が維持される。
- LotCommon 相当フィールドが全遷移を通じて不変である。
- 失敗時に shrink 後のコマンド列が再現手順として読める。
- 既存の固定遷移 property（`LotPropertyTests.fs`）は削除しない。
