# 非同期処理仕様 — GitHubからAzureへのAIレビューdispatch

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| 対象 | GitHub `verify`完了からService Bus受信、Git取込み、AI review結果通知まで |
| 状態 | Phase 4 complete / Phase 5A implemented locally / Phase 5B contract approval pending |

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
- base syncよりreviewが先に届いた場合、forceや別baseで続行しない。Phase 5AではGitHubとAzureのbaseが
  requestのbase SHAへ到達した場合だけ限定branchを作る。
- non-fast-forwardは自動解消しない。

## 5. 現在の物理contract

producerはworkflow内の固定`jq`式、consumerはGoのnative typeをmessage contractとして使用する。Phase 3で
`base-sync-request`と`review-request`のdecode、必須field、repository、workflow、SHA、`MessageId`対応を
consumerに実装し、実際のGitHub dispatchからService Bus受信・保存まで確認した。

producerが1つ、consumerが1つでnative typeによる検証で境界を守れるため、独立JSON Schema、fixture、
Python validatorは追加していない。今後追加するのは、producer/consumerが増えてnative typeだけでは
public/private repository間のdriftを検出できなくなった場合に限る。その場合もSchema自身を試すだけの
fixture testにはせず、producerとconsumerの双方が同じSchemaを利用する。

Phase 5Bではprivate IaC内のproducer/consumerが増えるため、独立JSON Schemaではなく、同じGo packageの
native typeを正として次の2種類を追加する。queue messageへproviderのraw response、finding本文、credentialを
含めない。

### `review-work`

PR head取込み完了後にdispatch controllerがAI Jobへ送る。

- `schemaVersion`: `1`
- `eventType`: `review-work`
- `occurredAt`
- `github.repositoryId`、`github.pullRequestNumber`、`github.baseSha`、`github.headSha`
- `azureRepos.sourceRef`: `refs/heads/github-pr/<number>/<full-head-sha>`
- `azureRepos.targetRef`: `refs/heads/github-main`

`MessageId`はrepository ID、Pull Request番号、head SHAから決定的に生成する。AI Jobはref形式、base/head SHA、
Azure Reposのremote refを再検証し、messageからrepository URLや任意refを受け取らない。

### `review-result-ready`

Claude/Kiro両方の検証済み結果をTableへ保存した後、AI JobがPR controllerへ送る。

- `schemaVersion`: `1`
- `eventType`: `review-result-ready`
- `occurredAt`
- `github.repositoryId`、`github.pullRequestNumber`、`github.baseSha`、`github.headSha`
- `azureRepos.sourceRef`、`azureRepos.targetRef`
- `providers`: 常に`claude`と`kiro`

`MessageId`はreview-workと同じidentityから決定的に生成する。PR controllerはTableから両providerの正規化済み
結果を取得して再検証し、Azure Reposのsource/headとtarget/baseが一致する場合だけPull Requestを作成する。
同じsource/targetのactive Pull Requestがあれば再利用し、重複作成しない。

`promotion-request`はproducer/consumer実装時までfieldを固定しない。

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

## Phase 5A review-request処理

consumerは`review-request`を保存した後、次の順で処理する。

1. Pull Request番号から固定のGitHub `refs/pull/<number>/head`を参照し、検証済みhead SHAと照合する。
2. GitHub `main`の現在SHAがrequestのbase SHAと一致することを確認する。先へ進んでいればstale no-opとする。
3. Azure mapped baseがrequestのbase SHAと一致することを確認する。未到達または分岐ならsettleせず停止する。
4. `github-pr/<number>/<full-head-sha>`が無ければ通常pushで作成する。同一SHAならno-op、異なるSHAなら停止する。
5. push後のremote refがhead SHAと一致した場合だけService Bus messageを完了する。

Azure Pull Requestの作成、AI review、古いAzure Pull Requestの整理はPhase 5Bで扱う。

## Phase 5B AI reviewとAzure Pull Request

1. dispatch controllerが限定branch作成後に`review-work`を送信する。stale no-opでは送信しない。
2. Azure Repos Read-only identityのone-shot AI Jobがsource/baseを再取得し、同じ差分をClaudeとKiroへ渡す。
3. ClaudeはMessages APIのstructured output、Kiroはheadless modeのJSON-only responseを使う。
4. provider別adapterは`summary`と`findings`だけへ正規化し、providerごとにTableへ保存する。
5. 両providerが揃った場合だけ`review-result-ready`を送る。再配信時は保存済みproviderを再実行しない。
6. PR controllerは結果とGit refを再検証し、限定branchから`github-main`へのAzure Pull Requestを1件作る。

両providerを必須とし、片方だけの結果ではPull Requestを作らない。provider失敗はwork messageをsettleせず
再配信に任せ、delivery上限到達後はDLQで人間が確認する。

### provider結果の最小contractと上限

- provider: `claude`または`kiro`
- summary: UTF-8、2,000 byte以下
- findings: providerごとに最大20件
- finding: `level`（`error`または`warning`）、repository相対`path`、1始まりの`line`、`message`、任意の`suggestion`
- messageは2,000 byte以下、suggestionは4,000 byte以下、provider result全体は48 KiB以下
- pathはhead側の変更fileに含まれ、path traversalでなく、lineはhead側実fileの範囲内でなければならない

raw provider response、prompt、provider credentialは永続化しない。PR説明へ出す文字列はcontrol characterを
拒否し、Markdownとしてescapeする。このfindingは人間向け情報であり、自動merge、vote、品質gateには使わない。

初期上限はdiff 512 KiB、Claude出力4,096 token・5分、Kiro headless 15分、Job全体25分とする。Claude modelは
構成値で明示し、Kiroは契約accountのdefault modelを使う。上限超過は切り詰めて続行せず失敗とし、課金と精度の
実測後に変更する。
