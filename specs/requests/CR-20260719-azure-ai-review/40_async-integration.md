# 非同期処理仕様 — GitHubからAzureへのAIレビューdispatch

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| 対象 | GitHub `verify`完了からService Bus受信、Git取込み、Azure PipelineによるAI reviewまで |
| 状態 | Phase 5A・5B・6 complete / Phase 7設計approved・実装中 |

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

Phase 5BはPhase 5Aが作成したAzure branch更新をAzure Pipelineのrepository resource triggerで直接受ける。
追加のService Bus message、Table entity、Go native type、独立JSON Schemaは作らない。trigger refとcommitは
Azure Pipelinesが提供する`Build.SourceBranch`と`Build.SourceVersion`を使用し、private IaCのdefault branchに
あるtrusted YAMLが固定規則と照合する。

Phase 7はAzure `review-target/*` branch更新をtrusted Pipelineが直接受け、
`promotion-request`、Service Bus queue、consumer stateを追加しない。

## 6. 失敗時の扱い

dispatchはGitHub API、OIDC token exchange、Service Bus sendのtimeout/HTTP errorでjobを失敗させる。
既存`verify`の結論は変更しない。

consumerは契約違反messageをDLQへ隔離する。Table保存、Azure DevOps token取得、Git同期、message完了の
いずれかが失敗した場合は成功扱いにせず、settleされなかったmessageをService Busのdelivery上限内で
再配信する。base同期は再配信時もbranchの実状態から安全に再判定する。

Phase 5B/6 Pipelineでは、trigger/ref/SHA不一致、選択したproviderの失敗、AIによる保護pathまたはsymlinkの
変更、review中のsource/base更新、Azure PR作成失敗のいずれもrunを失敗させる。AI出力は一時fileにだけ保存し、
失敗時に副作用を続行しない。Pipelineの再実行時は同じsource/targetのactive Pull Requestを検索して再利用し、
同一Pull Requestを重複作成しない。
default Claudeによるlive runでAzure Repos Pull Request作成後のsource/target/status read-backまで確認し、
Phase 5Bを完了した。

DLQの定期reconciliation、correlation IDを用いた通知、retentionの最終値は未実装であり、shadow rollout前に
実測した負荷・障害影響・費用を提示して承認する。

## 品質ゲート化対応表

| ID | 現在の実装・確認 | 将来 |
| --- | --- | --- |
| AAR-EVT-01/02 | `actionlint`、`shellcheck`、workflow review、実Service Bus dispatch、PR importからPipeline起動までのlive確認 | 追加なし。contract変更時だけ再確認 |
| 発生源再検証 | 固定workflowのcode review、過去runのAPI read-back、実messageのconsumer受信、Pipelineのtrigger ref/SHA/checkout HEAD一致をlive確認 | 追加なし。trigger変更時だけ再確認 |
| duplicate | `MessageId`とTable RowKey。実message再送で保存済み扱いを確認 | 追加なし。契約変更時だけ再確認 |
| reorder/stale | 一時bare repositoryで各Git状態を確認し、実環境でbase branch、PR branch、Azure PRのsource/target/statusをread-back | promotion実装時にexpected baseを確認 |
| DLQ/reconciliation | 契約違反DLQとdelivery上限を実装。適用後queue/DLQ空を確認 | 定期reconciliationと通知はshadow rollout前 |

## Phase 5A review-request処理

consumerは`review-request`を保存した後、次の順で処理する。

1. Pull Request番号から固定のGitHub `refs/pull/<number>/head`を参照し、検証済みhead SHAと照合する。
2. GitHub `main`の現在SHAがrequestのbase SHAと一致することを確認する。先へ進んでいればstale no-opとする。
3. Azure mapped baseがrequestのbase SHAと一致することを確認する。未到達または分岐ならsettleせず停止する。
4. `github-pr/<number>/<full-head-sha>`が無ければ通常pushで作成する。同一SHAならno-op、異なるSHAなら停止する。
5. push後のremote refがhead SHAと一致した場合だけService Bus messageを完了する。

AI review、修正案とAzure Pull Requestの作成はPhase 5B/6のtrusted Pipelineで扱う。

## Phase 5B/6 AI review、修正案とAzure Pull Request

1. Phase 5A controllerが`github-pr/<Pull Request番号>/<full-head-sha>`を新規作成する。
2. private IaCのAzure Pipelineがstaging repositoryの`github-pr/*`更新をresource triggerとして受ける。
3. Pipelineはprivate IaCのdefault branchにあるYAMLを使用し、trigger commitと`github-main`を別々にcheckoutする。
4. 固定shellがbranch形式、末尾SHA、`Build.SourceVersion`、checkout HEADの一致を確認する。
5. 検証済みheadを`.git`もcredentialも無い一時directoryへ展開し、選択したproviderへprivate側の固定promptと
   base差分だけを渡す。shell、Git、web、MCP、外部directoryの操作は許可しない。
6. 選択したproviderは人間向けのplain Markdownを返し、既存仕様の範囲で安全な場合だけ通常の実装fileへ
   最小の修正案を作る。仕様、API contract、CI、品質gate、agent設定、script、symlinkは変更しない。
7. AI processと分離した固定stepが、保護pathとsymlink、source/base SHA不変、AI commitのparentを検証する。
   applicationのbuild/testは、Azure merge後のGitHub promotion Pull Requestで一度だけ実行する。
8. 固定stepがAI修正案を`ai-fix/<Pull Request番号>/<full-head-sha>/<provider>`、人間review targetを
   `review-target/<Pull Request番号>/<base-sha>`へ新規作成し、両branch間のactive Pull Requestを検索する。
   無ければ作成し、存在すれば説明を更新して再利用する。

`System.AccessToken`は最後のbranch/PR作成stepだけに環境変数として渡し、provider stepには見せない。AI出力は
Pull Request説明の文字列としてだけ使用し、shell、Git ref、path、vote、自動完了、merge、policy bypassへ
変換しない。選択したproviderが失敗した場合はPull Requestを作成しない。結果Schema、provider adapter、
result queue、Table state、専用PR controllerは作らない。

## Phase 7 GitHub promotion

この設計は2026-07-22に人間承認済みである。private実装と外部権限は、public再帰除外のbootstrap反映後に変更する。

1. 別のtrusted Pipelineが`review-target/<Pull Request番号>/<base SHA>`更新を受ける。branch初回作成はno-op、
   2 parentのmerge commitだけを処理する。
2. `System.AccessToken`を持つ固定検証stepが、completed Azure PR、source/target/merge SHA、no-fast-forward、
   policy bypassなし、blocking policy成功、人間vote、merge parentをAzure APIからread-backする。
3. source branch形式から元GitHub PR番号、検証済みhead SHA、providerを復元し、proposal SHAが検証済みhead自身、
   またはそのheadを唯一のparentとする1 commitであることを確認する。
4. GitHub App installation token取得後、元GitHub PRのopen状態とbase/head SHA、現在の`main` SHAを再確認する。
   Azure review時のbase/headから進んでいれば、branch作成やforceをせず停止する。
5. 固定stepだけがimmutableな`ai-promotion/<Pull Request番号>/<Azure merge SHA>`を通常pushし、同じbranchと
   `main`向けPull Requestを冪等に作成または再利用する。AI出力、元PRのtitle/body、Azure PR説明は実行入力や
   promotion PR説明に使用しない。
6. promotion Pull Requestは既存GitHub Actions `verify`を実行する。GitHub APIでhead branch、Publisher Appの
   Bot user ID、head repositoryを照合できた場合だけAI review dispatchから除外し、prefixだけを名乗る通常PRは
   除外しない。verifyと既存main rulesetを通過後も、mergeは人間だけが行う。
