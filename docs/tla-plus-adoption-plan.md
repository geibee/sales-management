# TLA+ 導入計画

## 目的

TLA+ を、販売管理ドメインの「時間変化」「並行実行」「失敗時の再試行」を検証するための仕様記述として導入する。既存の F# 型、FsCheck、Schemathesis と役割を分け、実装テストでは見落としやすい状態遷移と並行性の仕様を小さく検証する。

## 導入判断

このリポジトリでは、TLA+ を入れる余地がある。特に次の領域は、型や単体テストだけでは仕様の抜けを検出しにくい。

- ロット、販売案件、予約、委託のライフサイクル
- 楽観ロックによる同時更新の競合制御
- Outbox の保存、送信、再試行、重複送信
- バッチ処理の chunk progress と二重実行防止
- 状態遷移 API と DB 永続化の間の整合性

一方で、TLA+ をすべてのドメイン型へ広げる必要はない。F# の判別共用体で表現できている「状態ごとの必須データ」は、まず F# 型と FsCheck に任せる。TLA+ は、状態が時間とともに変わるシナリオに限定する。

## 既存検査との役割分担

| 手段 | 主な対象 | この repo での責務 |
| --- | --- | --- |
| F# 型 | ドメイン構造 | 状態ごとの必須データ、ありえない組み合わせの排除 |
| FsCheck | 純粋関数 | 往復性、順序性、不変性、smart constructor 境界 |
| Schemathesis | HTTP 契約 | OpenAPI と API 実装の整合 |
| TLA+ | 時間変化・並行性 | 遷移列、競合、再試行、最終状態、失敗時の安全性 |

## 配置方針

```
specs/
└── tla/
    ├── README.md
    ├── LotLifecycle.tla
    ├── LotLifecycle.cfg
    ├── OptimisticLock.tla
    ├── OptimisticLock.cfg
    ├── OutboxProcessing.tla
    └── OutboxProcessing.cfg

tools/
└── formal/
    └── ci.sh
```

初期導入では `specs/tla/README.md` と 1 つの小さい model だけを追加する。`tools/formal/ci.sh` は、TLA+ 実行方法が安定してから追加する。

## フェーズ計画

### Phase 1: ロット状態遷移モデル

最初の対象は `LotLifecycle.tla` とする。

対象状態:

- `manufacturing`
- `manufactured`
- `shipping_instructed`
- `shipped`
- `conversion_instructed`

対象アクション:

- `CompleteManufacturing`
- `CancelManufacturingCompletion`
- `InstructShipping`
- `CompleteShipping`
- `InstructItemConversion`
- `CancelItemConversionInstruction`

検証する不変条件:

- ロットは常に定義済み状態のいずれかにある。
- `shipped` は終端状態であり、別状態へ戻らない。
- `conversion_instructed` から cancel すると `manufactured` に戻る。
- `manufacturing` から直接 `shipping_instructed` や `shipped` には到達しない。
- 許可された遷移以外で状態は変化しない。

完了条件:

- TLC で finite scope の model check が通る。
- 既存 `LotPropertyTests.fs` の往復性・順序性と対応する invariant が README に説明されている。
- 反例が出た場合に、どの API / workflow に対応するか追える。

### Phase 2: 楽観ロックモデル

次の対象は `OptimisticLock.tla` とする。

対象シナリオ:

- 2 つ以上の client が同じ aggregate と同じ version を読む。
- 片方が更新に成功し、version が増える。
- 古い version を持つ client の更新は競合として失敗する。

検証する不変条件:

- 同じ version に対する成功更新は最大 1 件。
- 成功した更新だけが version を進める。
- 失敗した更新は aggregate state を変えない。
- version は単調増加する。

完了条件:

- `OptimisticLockConflictTests.fs` の期待と TLA+ invariant が対応している。
- API 実装の 409 応答が TLA+ 上の conflict と対応している。

### Phase 3: Outbox 処理モデル

対象は `OutboxProcessing.tla` とする。

対象状態:

- `pending`
- `publishing`
- `published`
- `failed`

対象アクション:

- domain event を outbox に保存する。
- processor が pending event を取得する。
- publish が成功する。
- publish が失敗し、retry 可能状態になる。
- retry 上限に達する。

検証する不変条件:

- 保存済み event は、送信済みまたは失敗扱いになるまで消えない。
- `published` は未送信状態へ戻らない。
- 同一 event の重複 publish を許すか禁止するかを仕様上明示する。
- processor が途中停止しても、pending event は再取得可能である。

完了条件:

- 実装上の outbox status と TLA+ state 名が対応している。
- 重複送信の扱いが README に明記されている。

### Phase 4: バッチ chunk progress モデル

対象は batch job の分割処理と再実行。

検証する不変条件:

- 処理済み chunk は未処理へ戻らない。
- failed chunk の retry は他 chunk の progress を壊さない。
- 同じ job parameter の二重実行を許すか禁止するかが明確である。
- 途中停止後の再開で、完了済み chunk が二重適用されない。

完了条件:

- `BatchChunkProgressTests.fs` と `BatchJobLifecycleTests.fs` の期待と対応する。

## CI 連携方針

初期は必須ゲートにしない。TLA+ model は設計メモとして導入し、安定後に CI に組み込む。

段階:

1. 手動実行用の README を追加する。
2. `tools/formal/ci.sh` を追加する。
3. 結果を `ci-results/sarif/formal.sarif` に変換する。
4. `apps/api-fsharp/ci.sh` の SARIF merge に formal run を追加する。
5. warning 扱いで運用し、反例の品質が安定してから error へ昇格する。

SARIF の ruleId 案:

- `tla.invariant.violation`
- `tla.deadlock`
- `tla.temporal.violation`
- `tla.model.invalid`

## 運用ルール

- TLA+ model は実装のコピーにしない。仕様上の状態とアクションを小さく保つ。
- 1 model につき検証したい問いを 1 つから 3 つに絞る。
- finite scope を明記する。
- model の状態名は API / DB / F# 型のどれかに対応づける。
- 反例は、F# テストまたは実装タスクへ変換できる粒度にする。
- DSL を変更した場合、関連する TLA+ model の更新要否を確認する。

## 導入しない対象

初期導入では、次は対象外とする。

- 金額計算の正しさ
- UI の画面遷移
- OpenAPI schema の validation
- DB migration の構文検証
- すべての domain type の完全な Alloy 的構造検査

これらは、既存の unit test、FsCheck、Schemathesis、migration test で扱う。

## 最初の PR 案

最初の PR は次の範囲に限定する。

- `specs/tla/README.md`
- `specs/tla/LotLifecycle.tla`
- `specs/tla/LotLifecycle.cfg`

最初の model で検証する invariant は 3 つだけにする。

- ロット状態は定義済み状態に限られる。
- `shipped` から戻らない。
- `conversion_instructed` の cancel は `manufactured` に戻る。

この範囲なら、既存の `LotWorkflows.fs` と `LotPropertyTests.fs` に対応づけやすく、導入コストに対して学習効果が高い。

## 成功基準

- TLA+ model が、実装前の仕様レビューに使える。
- TLA+ の反例が F# テスト追加または実装修正へ直接つながる。
- CI に入れても通常開発の速度を落とさない。
- DSL、F# 型、PBT、TLA+ の責務分担が README で説明されている。

## リスク

- model が大きくなりすぎると、状態爆発で CI に向かない。
- 実装詳細を写しすぎると、仕様検査ではなく二重実装になる。
- TLA+ に慣れていない開発者には、反例の読み方が負担になる。
- model と F# 実装の対応が曖昧だと、仕様が放置されやすい。

## 次の一手

`LotLifecycle.tla` の最小 model を追加し、既存のロット PBT と対応づける。その後、楽観ロックへ広げるか、Outbox へ広げるかを、直近の開発リスクに応じて選ぶ。
