# 非同期処理仕様 — GitHubからAzureへのAIレビューdispatch

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| 対象 | GitHub `verify`完了からService Bus受信、base同期まで |
| 状態 | Phase 4 complete / Phase 5以降は未実装 |

## 1. トリガ

| ID | 発生条件 | 通知 | 発生させない条件 |
| --- | --- | --- | --- |
| AAR-EVT-01 | public Pull Requestの`verify`が成功 | `review-request` | failure、cancelled、skipped、対象外repository |
| AAR-EVT-02 | public `main` pushの`verify`が成功 | `base-sync-request` | `main`以外、failure、cancelled、skipped |

dispatchはdefault branch上の固定workflowを使う。Pull Request code、artifact、cacheを読み込まない。

## 2. 通知する意味情報

### `base-sync-request`

- eventの版と種類
- 発生時刻
- GitHub repository identity
- workflow run ID、attempt、event、conclusion
- `main`のsource ref
- verified commit SHA

### `review-request`

- eventの版と種類
- 発生時刻
- GitHub repository identity
- Pull Request番号
- workflow run ID、attempt、event、conclusion
- base ref / SHA
- head ref / SHA
- 成功したworkflow runに結び付いたhead SHA

Pull Requestのhead refは表示用metadataである。Azure側のdestination ref、shell command、Git refspecをhead refから生成しない。

## 3. 発生源の再検証

dispatchはevent payloadだけを信用せず、job単位の`GITHUB_TOKEN`でGitHub APIから次を再取得して照合する。Workflow Run APIの`pull_requests`は空になり得るため、成功runのhead SHAに関連するPull Request一覧から、open、base=`main`、head SHA一致の1件を選ぶ。

- repository
- workflow run ID / attempt
- workflow名とworkflow file
- eventとsuccess conclusion
- Pull Request番号とopen状態
- base/head SHA

不一致はService Busへ送らずfail closedとする。

## 4. 配信特性

Service Busはat-least-onceとして扱い、重複と順序逆転を正常系として想定する。Phase 3/4では次を実装した。

- producerが内容から決定的な`MessageId`を生成し、consumerがmessage本文との対応を再検証する。
- `MessageId`をTable Storageの一意なRowKeyとして保存し、再配信時のconflictを保存済みとして扱う。
- 保存後もbase同期をbranchの実状態から再判定し、保存とpushの間で停止しても同じpushを重複させない。
- 契約違反messageは理由を付けてDLQへ移し、Table保存またはbase同期の一時失敗はsettleせず再配信に任せる。
- queueのmax deliveryは5、message TTLは1日とし、Job executionは同時に1つまでとする。
- base同期ではbranch未作成、同一SHA、fast-forward、順序逆転した古いrequest、分岐を区別し、force pushしない。

最低限の意味は次のとおりとする。

- 同一workflow run/attemptの再配信で同じ副作用を重複させない。
- 古いPull Request headの結果を新しいheadへ適用しない。
- base syncよりreviewが先に届いた場合、forceや別baseで続行しない。このPR import順序制御はPhase 5で実装する。
- non-fast-forwardは自動解消しない。

## 5. 現在の物理contract

producerはworkflow内の固定`jq`式、consumerはGoのnative typeをmessage contractとして使用する。Phase 3で
`base-sync-request`と`review-request`のdecode、必須field、repository、workflow、SHA、`MessageId`対応を
consumerに実装し、実際のGitHub dispatchからService Bus受信・保存まで確認した。

producerが1つ、consumerが1つでnative typeによる検証で境界を守れるため、独立JSON Schema、fixture、
Python validatorは追加していない。今後追加するのは、producer/consumerが増えてnative typeだけでは
public/private repository間のdriftを検出できなくなった場合に限る。その場合もSchema自身を試すだけの
fixture testにはせず、producerとconsumerの双方が同じSchemaを利用する。

未実装の`review-result`と`promotion-request`は、それぞれのproducer/consumer実装時までfieldを固定しない。

## 6. 失敗時の扱い

dispatchはGitHub API、OIDC token exchange、Service Bus sendのtimeout/HTTP errorでjobを失敗させる。
既存`verify`の結論は変更しない。

consumerは契約違反messageをDLQへ隔離する。Table保存、Azure DevOps token取得、Git同期、message完了の
いずれかが失敗した場合は成功扱いにせず、settleされなかったmessageをService Busのdelivery上限内で
再配信する。base同期は再配信時もbranchの実状態から安全に再判定する。

DLQの定期reconciliation、correlation IDを用いた通知、retentionの最終値は未実装であり、shadow rollout前に
実測した負荷・障害影響・費用を提示して承認する。

## 品質ゲート化対応表

| ID | 現在の実装・確認 | 将来 |
| --- | --- | --- |
| AAR-EVT-01/02 | `actionlint`、`shellcheck`、workflow review、実Service Bus dispatch | PR import後のend-to-end確認 |
| 発生源再検証 | 固定workflowのcode review、過去runのAPI read-back、実messageのconsumer受信 | provider接続時の入力境界確認 |
| duplicate | `MessageId`とTable RowKey。実message再送で保存済み扱いを確認 | 追加なし。契約変更時だけ再確認 |
| reorder/stale | 一時bare repositoryで各Git状態を確認し、実環境でbase branch作成とSHA一致を確認 | PR import順序制御はPhase 5 |
| DLQ/reconciliation | 契約違反DLQとdelivery上限を実装。適用後queue/DLQ空を確認 | 定期reconciliationと通知はshadow rollout前 |
