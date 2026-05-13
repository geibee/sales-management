# FsCheck 改善提案

## 目的

FsCheck を「ドメイン純粋関数の性質検証」として強化する。現状の PBT は高速で安定しているが、generator の入力空間が狭く、正常系の往復性・順序性・不変性に寄っている。境界値、不正値、状態機械、HTTP 変換前後の契約を段階的に追加し、ドメインモデル変更時の回帰検出力を高める。

## 現状

- `LotPropertyTests.fs` にロット状態遷移の PBT がある。
- `SalesCasePropertyTests.fs` に販売案件、予約、委託の PBT がある。
- `Category=PBT` で切り出し可能。
- `dotnet test apps/api-fsharp/tests/SalesManagement.Tests --filter "Category=PBT" --no-restore` は `15/15 passed`。
- `Support/Generators.fs` は HTTP 用 generator の足場だが、現時点では実テストから使われていない。

## 良い点

- DB、HTTP、認証、外部 API に依存せず、純粋関数だけを検査している。
- 実行時間が短く、ローカル TDD に向いている。
- 往復性、順序性、不変性という PBT に適した性質を既に扱っている。
- 型で表現した状態遷移をテストしているため、DSL 由来のドメイン設計と相性が良い。

## 主な課題

1. generator の分散と重複

   `LotPropertyTests.fs` と `SalesCasePropertyTests.fs` にロット系 generator が重複している。今後 stateful PBT や HTTP PBT を追加すると、テストデータ定義がさらに分散する。

2. 入力空間が狭い

   多くのフィールドが固定値で、実際に揺れているのは年、月、日、連番、金額の一部に限られる。仕様上重要な enum、nullable、NonEmptyList の複数要素、数量境界などが十分に探索されていない。

3. 境界値と不正値の性質が少ない

   smart constructor や API validation が扱う下限値、空文字、空配列、不正 enum、負数などの検査が PBT として体系化されていない。

4. shrink 後の反例が読みやすいとは限らない

   現在の generator は domain object を直接生成するため、失敗時の最小反例が業務上意味のある識別子や JSON として読める保証が弱い。

5. 状態機械 PBT が未整備

   現状は決め打ちの遷移列を property として検証している。任意のコマンド列に対して「許可された遷移だけ成功し、不正遷移は表現不能またはエラーになる」という検査にはまだなっていない。

## 改善方針

- FsCheck はドメイン層の性質検証を主責務にする。
- Schemathesis と重複させず、HTTP 契約ではなく純粋関数・型・DTO 変換の性質を検査する。
- generator は再利用可能な Support 層へ移す。
- 正常値 generator と不正値 generator を分ける。
- まず狭い純粋関数から Red / Green し、状態機械 PBT は最後に広げる。

## 提案タスク

### F1: generator を Support に集約する

`LotPropertyTests.fs` と `SalesCasePropertyTests.fs` の generator を `Support/Generators.fs` へ段階的に移す。

候補:

- `Domain.Lot.detailGen`
- `Domain.Lot.commonGen`
- `Domain.Lot.manufacturedGen`
- `Domain.SalesCase.commonGen`
- `Domain.Appraisal.commonGen`
- `Domain.Reservation.commonGen`
- `Domain.Consignment.consignorInfoGen`
- `Domain.dateGen`

完了条件:

- ロット共通 generator の定義が 1 箇所になる。
- 既存 15 件の PBT がすべて通る。
- generator の module 名がドメイン構造に沿っている。

### F2: 入力空間を広げる

固定値中心の generator に、業務上意味のある揺らぎを追加する。

追加候補:

- `ItemCategory`: `General` 以外も生成する。
- `PremiumCategory`: `None` と `Some` の両方を生成する。
- `InspectionResultCategory`: `None` と `Some` の両方を生成する。
- `LotCommon.Details`: 1 件だけでなく 2 から 5 件程度の NonEmptyList を生成する。
- `DivisionCode`, `DepartmentCode`, `SectionCode`: 複数値を生成する。
- `Quantity`: `0.001`, `1.0`, 大きめの値を含める。
- `Amount`: `0`, `1`, 大きめの値を含める。
- `DateOnly`: 月末近辺、うるう年、年境界を含める。

完了条件:

- 既存 property が広い入力空間でも通る。
- FsCheck の反例が業務上読める範囲に保たれる。

### F3: smart constructor の境界値 property を追加する

対象:

- `Amount.tryCreate`
- `Quantity.tryCreate`
- `Count.tryCreate`
- `PositiveInt.tryCreate`
- `NonEmptyString.tryCreate`
- `LotNumber.tryParse`
- `SalesCaseNumber` parser / formatter

性質:

- 下限値以上は `Ok`。
- 下限値未満は `Error`。
- formatter と parser の round-trip が成立する。
- 空文字、不正形式、桁不足、余分な区切りは parse できない。

完了条件:

- 境界値の仕様が property 名で読める。
- 既存の example-based validation test と責務が重複しすぎない。

### F4: DTO / domain 変換の property を追加する

`Api` 層の validation は HTTP なしで検査できる部分がある。ここを FsCheck で補強する。

候補:

- `CreateLotRequest` の正常 DTO は `validateCreateLotRequest` で `Ok` になる。
- `details=[]` は必ず validation error になる。
- `count < 1` は必ず validation error になる。
- `quantity < 0.001` は必ず validation error になる。
- `lotNumber` の format / parse が往復する。

完了条件:

- HTTP を立てずに API 境界の validation 契約を検査できる。
- Schemathesis の schema 改善と同じ制約を、F# 側でも property として表現できる。

### F5: 状態機械 PBT を導入する

最初はロット状態遷移に限定する。

詳細な実装計画は [`stateful-pbt-fscheck-implementation-plan.md`](./stateful-pbt-fscheck-implementation-plan.md) を参照する。

モデル:

- `Manufacturing`
- `Manufactured`
- `ShippingInstructed`
- `Shipped`
- `ConversionInstructed`

コマンド例:

- `CompleteManufacturing`
- `CancelManufacturingCompletion`
- `InstructShipping`
- `CompleteShipping`
- `InstructItemConversion`
- `CancelItemConversionInstruction`

性質:

- 許可された遷移列では、共通情報が維持される。
- cancel 系は直前の対象状態に戻る。
- shipped まで到達した後に、戻し操作が存在しないことをモデルで確認する。
- 実装不能な遷移は型で表現できないか、API 層では明示的に error になる。

完了条件:

- 既存の固定遷移 property を置き換えず、まず追加で導入する。
- 反例にコマンド列が表示され、再現手順として読める。

### F6: seed と実行回数の運用を決める

ローカル TDD と CI で設定を分ける。

提案:

- ローカル既定は FsCheck.Xunit の標準回数を維持する。
- CI では PBT 専用ジョブで `MaxTest` を増やす。
- flaky な property は seed を記録し、再現コマンドを PR コメントまたは SARIF に残す。

完了条件:

- 失敗した property の seed と shrink 後反例が CI ログから追える。
- 実行時間が CI 全体のボトルネックにならない。

## 優先順位

1. F1: generator 集約
2. F3: smart constructor 境界値 property
3. F4: DTO / domain 変換 property
4. F2: 入力空間拡張
5. F5: 状態機械 PBT
6. F6: seed と実行回数の運用

## 成果指標

- `Category=PBT` のテストが 15 件から段階的に増える。
- generator 重複が解消される。
- smart constructor の境界値が property として明文化される。
- `POST /lots` 相当の validation ルールを HTTP なしで検査できる。
- 状態遷移の新規追加時に、既存 property が回帰を検出する。

## リスク

- generator を広げすぎると、失敗時の反例が読みにくくなる。
- 状態機械 PBT は設計を誤ると実装と同じロジックをテスト側に複製するだけになる。
- CI の `MaxTest` を増やしすぎると、短時間で回す TDD の利点が落ちる。

## 次の一手

最初の PR では generator の重複解消だけを行う。`LotPropertyTests.fs` と `SalesCasePropertyTests.fs` のロット共通 generator を `Support/Generators.fs` に移し、既存 `Category=PBT` が 15 件すべて通ることを確認する。
