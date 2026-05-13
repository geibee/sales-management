# Stateful PBT 実装計画

作成日: 2026-05-13

## 位置づけ

本ドキュメントは [`docs/pbt-fscheck-improvement-proposal.md`](./pbt-fscheck-improvement-proposal.md) の F5「状態機械 PBT」を具体化する実装計画である。

既存の FsCheck 改善提案は、generator 集約、境界値 property、DTO/domain 変換 property を含む全体提案である。本計画はその中から stateful PBT だけを切り出し、ロット状態遷移を最初の対象として、Red / Green / Refactoring で実装するための作業単位を定義する。

## 目的

現在の `LotPropertyTests.fs` は、決め打ちの遷移列に対する往復性、順序性、不変性を検査している。これは有効だが、任意のコマンド列に対して、モデルと実装が同じ状態に到達するかは検査していない。

stateful PBT では、コマンド列を生成し、抽象モデルと実装対象を同期させながら検査する。

検査したい性質:

- 許可された遷移だけが成功する。
- 不正な遷移は、ドメイン層では型で表現不能になる。
- API 層または `InventoryLot` wrapper 経由では、不正な遷移が明示的な error になる。
- どの遷移列でも `LotCommon` は維持される。
- cancel 系は対象状態の直前状態に戻る。
- `Shipped` 到達後は、戻し操作が存在しない。
- 反例はコマンド列として読める。

## 対象範囲

### 初期対象

最初は `InventoryLot` のロット状態遷移だけを対象にする。

対象状態:

- `Manufacturing`
- `Manufactured`
- `ShippingInstructed`
- `Shipped`
- `ConversionInstructed`

対象 workflow:

- `completeManufacturing`
- `cancelManufacturingCompletion`
- `instructShipping`
- `completeShipping`
- `instructItemConversion`
- `cancelItemConversionInstruction`

対象ファイル:

- `apps/api-fsharp/src/SalesManagement/Domain/Types.fs`
- `apps/api-fsharp/src/SalesManagement/Domain/LotWorkflows.fs`
- `apps/api-fsharp/tests/SalesManagement.Tests/LotPropertyTests.fs`
- `apps/api-fsharp/tests/SalesManagement.Tests/Support/Generators.fs`
- 新規: `apps/api-fsharp/tests/SalesManagement.Tests/LotStateMachinePropertyTests.fs`

### 初期対象外

- DB 永続化を含む状態遷移
- HTTP API を叩く stateful PBT
- 複数ロットをまたぐ在庫引当
- 並行実行や線形化可能性
- Schemathesis の stateful testing

これらはロット単体の sequential stateful PBT が安定した後に検討する。

## 基本設計

### 実装方式

初期実装では、FsCheck の通常の `Gen<LotCommand list>` と `fold` で sequential stateful PBT を組む。

FsCheck には model-based/state machine testing 用の API もあるが、まずは次の理由で薄い自前 runner から始める。

- ロット状態遷移は純粋関数で閉じており、コマンド列を fold するだけで検査できる。
- テスト側の抽象を小さく保ち、反例のコマンド列を読みやすくできる。
- FsCheck の experimental API に依存する前に、必要なモデル形状を確定できる。

後続でコマンドの precondition、postcondition、shrink、並行性を広げる段階になったら、FsCheck の state machine API または別ライブラリへの移行を判断する。

### 1. モデルと実装状態を分離する

テスト側に実装ロジックを丸ごと複製しないため、モデルは最小限の抽象状態にする。

```fsharp
type LotModelState =
    | MManufacturing
    | MManufactured
    | MShippingInstructed
    | MShipped
    | MConversionInstructed
```

実装状態は `InventoryLot` を使う。

```fsharp
type LotTestState =
    { Model: LotModelState
      Actual: InventoryLot
      Common: LotCommon
      Trace: LotCommand list }
```

`Model` は遷移可否と到達状態だけを持つ。日付、共通情報、変換先情報の維持は `Actual` 側を検査して確認する。

### 2. コマンドを明示する

コマンドは shrink 後の反例として読める必要があるため、業務操作名をそのまま型にする。

```fsharp
type LotCommand =
    | CompleteManufacturing of DateOnly
    | CancelManufacturingCompletion
    | InstructShipping of DateOnly
    | CompleteShipping of DateOnly
    | InstructItemConversion of ConversionDestinationInfo
    | CancelItemConversionInstruction
```

`ToString()` または helper で、反例ログに業務名と主要パラメータが出るようにする。

### 3. pure workflow には不正遷移を渡さない

現在の pure workflow は状態ごとの型を入力に取る。

```fsharp
completeManufacturing : DateOnly -> ManufacturingLot -> ManufacturedLot
instructShipping : DateOnly -> ManufacturedLot -> ShippingInstructedLot
completeShipping : DateOnly -> ShippingInstructedLot -> ShippedLot
```

したがって、ドメイン層の不正遷移は「型で表現不能」である。この性質を崩さない。

一方、stateful PBT では任意コマンド列を扱う必要があるため、テスト側に `InventoryLot -> LotCommand -> Result<InventoryLot, LotTransitionError>` の薄い adapter を置く。

```fsharp
type LotTransitionError =
    | InvalidTransition of fromStatus: string * command: string

let applyCommand (command: LotCommand) (lot: InventoryLot) : Result<InventoryLot, LotTransitionError> =
    // InventoryLot の union case を pattern match し、許可された場合だけ pure workflow を呼ぶ。
```

この adapter は production code に入れない。まずは test helper として持ち、API 層の遷移 validation と重複する場合だけ共通化を検討する。

### 4. モデル遷移を小さく保つ

モデル遷移は「この状態でこのコマンドが許可されるか」と「成功時の次状態」だけを持つ。

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

日付や `LotCommon` のコピーはモデルに持たせない。そこまで持つと実装複製になりやすい。

## 実装ステップ

### S1: Support generator を先に整える

目的: stateful PBT が既存 generator の重複に依存しないようにする。

作業:

- `LotPropertyTests.fs` の `lotDetailGen`、`lotCommonGen`、`dateGen` を `Support/Generators.fs` に移す。
- `Arbitrary<LotCommon>` と `Arbitrary<DateOnly>` を Support 側から再利用できるようにする。
- 既存 `LotPropertyTests.fs` を Support generator 利用に差し替える。

検証:

```bash
dotnet test apps/api-fsharp/tests/SalesManagement.Tests --filter "Category=PBT" --no-restore
```

完了条件:

- 既存 PBT がすべて通る。
- generator 重複が増えていない。

### S2: コマンド型と generator を追加する

目的: 任意のロット操作列を生成できるようにする。

作業:

- `LotStateMachinePropertyTests.fs` を追加する。
- `LotCommand` を定義する。
- `Gen<LotCommand>` と `Gen<LotCommand list>` を定義する。
- 初期はコマンド列長を 0 から 20 程度に制限する。
- `CompleteManufacturing`、`InstructShipping`、`CompleteShipping` は `DateOnly` generator を使う。
- `InstructItemConversion` は小さな `ConversionDestinationInfo` generator を使う。

完了条件:

- コマンド列 generator が単独 property で shrink 可能である。
- 反例表示でコマンド名が読める。

### S3: model step と actual adapter を実装する

目的: 抽象モデルと実装 wrapper の結果を比較できるようにする。

作業:

- `stepModel : LotCommand -> LotModelState -> Result<LotModelState, LotTransitionError>` をテストに追加する。
- `applyCommand : LotCommand -> InventoryLot -> Result<InventoryLot, LotTransitionError>` をテストに追加する。
- `InventoryLot.statusString` を error message に使う。
- `InventoryLot.common` で `LotCommon` の維持を検査する。

完了条件:

- 許可遷移は `stepModel` と `applyCommand` が両方 `Ok` になる。
- 不正遷移は `stepModel` と `applyCommand` が両方 `Error` になる。

### S4: sequential stateful property を追加する

目的: 任意コマンド列に対して、モデルと実装が同じ可否・同じ到達状態になることを検査する。

property:

```text
任意の初期 common と任意のコマンド列について、
Manufacturing から順に適用したとき、
モデル遷移と actual 遷移の成功/失敗が一致し、
成功時の状態が一致し、
LotCommon が常に維持される。
```

初期実装では、不正コマンドに遭遇した時点で停止してよい。次段階で「不正コマンドは Error として観測し、状態を変えずに後続コマンドへ進む」方式に拡張する。

完了条件:

- `Category=PBT` に含まれる。
- 失敗時に seed と shrink 後のコマンド列が読める。
- 既存固定遷移 property は残す。

### S5: 不正遷移後も継続する property を追加する

目的: API 層に近い「不正操作は error だが、現在状態は変わらない」を検査する。

property:

```text
任意のコマンド列について、
不正遷移は Error になり、
その後も直前の状態から次コマンドを評価できる。
```

これにより、たとえば `ShippingInstructed` に `CancelManufacturingCompletion` を送っても、状態が破壊されないことを確認できる。

完了条件:

- 不正遷移を含むコマンド列が十分に生成される。
- `Error` 後の状態維持が検査される。

### S6: API 層への拡張可否を判断する

目的: pure domain の stateful PBT と API route validation の責務境界を確認する。

判断観点:

- `InventoryLot -> LotCommand -> Result<InventoryLot, LotTransitionError>` 相当の adapter が API 層にも存在するか。
- 既存の `StateTransitionParamTests` や `LotLifecycleTests` と責務が重複しないか。
- DB version を含む楽観ロック検査は integration test に残すべきか。

この段階では API stateful PBT を実装しない。実装する場合は別計画に分離する。

## テストファイル構成

初期案:

```text
apps/api-fsharp/tests/SalesManagement.Tests/
├── LotPropertyTests.fs
├── LotStateMachinePropertyTests.fs
└── Support/
    └── Generators.fs
```

`SalesManagement.Tests.fsproj` は F# の compile order を持つため、追加ファイルは `LotPropertyTests.fs` の近く、かつ `Support/Generators.fs` より後に配置する。

## FsCheck 設定

ローカル:

- `FsCheck.Xunit` の既定回数から始める。
- コマンド列長は短くする。
- 失敗時のコマンド列可読性を優先する。

CI:

- まず既存 `Category=PBT` に含める。
- 実行時間が増えたら PBT 専用ジョブで `MaxTest` を増やす。
- seed は CI ログまたは SARIF へ残す方針を別途検討する。

## TDD の進め方

1. 探索: 既存 `LotPropertyTests.fs` と `LotWorkflows.fs` の現在仕様を確認する。
2. Red: `LotStateMachinePropertyTests.fs` に最小のモデル/actual 一致 property を追加する。
3. Green: test helper の `applyCommand` と generator を最小実装する。
4. Refactoring: generator を Support へ寄せ、コマンド表示と shrink 後の読みやすさを改善する。
5. Red: 不正遷移後も状態が維持される property を追加する。
6. Green: error と状態維持の比較を追加する。
7. Refactoring: API 層に拡張すべきかを判断し、必要なら別計画へ切り出す。

## リスクと対策

| リスク | 対策 |
| --- | --- |
| モデルが実装と同じロジックになる | モデルは抽象状態と遷移可否だけに限定し、日付や common copy は actual 検査に寄せる。 |
| 反例が読みにくい | `LotCommand` の表示を業務操作名にし、コマンド列長を制限する。 |
| shrink で意味の薄い反例になる | 日付や変換先情報は単純な generator にし、まずコマンド順序の shrink を優先する。 |
| API 層と責務が混ざる | 初期実装は pure domain + test adapter に限定する。 |
| CI が遅くなる | 既定回数から始め、必要になったら PBT 専用ジョブへ分離する。 |

## 完了条件

- `LotStateMachinePropertyTests.fs` が追加されている。
- `Category=PBT` で stateful PBT が実行できる。
- 任意コマンド列に対して、モデルと actual の遷移可否が一致する。
- 成功遷移列では、到達状態と `LotCommon` 維持が検査される。
- 不正遷移では、error になり状態が維持される property がある。
- 失敗時に shrink 後のコマンド列が再現手順として読める。
- 既存の固定遷移 property は削除せず、stateful PBT の安定後に統合可否を判断する。
