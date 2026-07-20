# 非同期処理仕様 — GitHubからAzureへのAIレビューdispatch

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| 対象 | GitHub `verify`完了からService Bus通知まで |
| 状態 | Phase 2 implementing |

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

Service Busはat-least-onceとして扱い、重複と順序逆転を正常系として想定する。ただしconsumerとstateが未実装のため、具体的な冪等キー、retry、DLQ、reconciliationはPhase 3で実装と同時に確定する。

最低限の意味は次のとおりとする。

- 同一workflow run/attemptの再配信で同じ副作用を重複させない。
- 古いPull Request headの結果を新しいheadへ適用しない。
- base syncよりreviewが先に届いた場合、forceや別baseで続行しない。
- non-fast-forwardは自動解消しない。

## 5. 物理contractを固定する時期

現段階ではproducerが1つでconsumerが未実装なので、workflow内の固定`jq`式をmessageの実体とし、独立JSON Schema、fixture、Python validatorを置かない。

Phase 3でconsumerを作るときに次を行う。

1. `base-sync-request`と`review-request`をconsumerのnative typeとして定義する。
2. workflowから送った実messageをdeserializeするintegration testを作る。
3. native typeだけではpublic/private repository間のdriftを検出できない場合に限り、2種類のJSON Schemaを追加する。
4. Schemaを追加する場合は、producerとconsumerの双方が同じSchemaを実行時またはCIで利用する。Schema自身を試すだけのfixture testにはしない。

未実装の`review-result`と`promotion-request`は、それぞれのproducer/consumer実装時までfieldを固定しない。

## 6. 失敗時の扱い

現在のdispatchはGitHub API、OIDC token exchange、Service Bus sendのtimeout/HTTP errorでjobを失敗させる。既存`verify`の結論は変更しない。

consumer実装後は次を追加する。

- transportの一時障害だけを対象にした上限付きretry
- malformedまたは認可不一致messageの隔離
- DLQとreconciliation
- correlation IDを用いた通知

具体的な回数・時間は負荷と費用を確認してPhase 3で承認する。

## 品質ゲート化対応表

| ID | 現在 | consumer実装後 |
| --- | --- | --- |
| AAR-EVT-01/02 | `actionlint`、`shellcheck`、workflow review | 実Service Bus messageのintegration test |
| 発生源再検証 | 固定workflowのcode review、過去の実workflow runを使ったAPI read-back | consumer実装時の実message integration test |
| duplicate/reorder/stale | 未実装 | stateを含むconsumer test |
| DLQ/reconciliation | 未実装 | Azure PoC integration test |
