# AI レビュー・外部 worker 改善計画

- 作成日: 2026-07-15
- 最終更新日: 2026-07-21
- 状態: Phase 4完了。Phase 5AのPull Request head importをprivate IaCで実装中。kill switchは有効
- 次の作業: Phase 5Aをレビュー・適用し、open GitHub Pull Requestで限定branch取込みを確認する

## 0. 次セッションの開始位置

1. repository root の `AGENTS.md` と `LESSONS.md` を読む。
2. 本書と `specs/requests/CR-20260719-azure-ai-review/` を読む。
3. `git status --short --branch` と `git log -1 --oneline` で作業状態を確認する。
4. public 側では `.github/workflows/ai-review-dispatch.yml` だけが実行コードであることを確認する。
5. Azure の具体的な resource 名、tenant/client/subscription ID、Azure DevOps の project/repository ID、deployment 履歴は private infrastructure repository を正とする。public repository へ複製しない。
6. `AI_REVIEW_DISPATCH_ENABLED` はPhase 3の実接続確認後に`true`へ変更済み。異常時は最初に`false`へ戻す。
7. Azure resource、identity、repository policy、課金設定を変更するときは、対象・費用・rollback を提示して別途承認を得る。
8. Phase 4は適用済み。Phase 5AはPR headの限定branch取込み、Phase 5BはClaude/KiroのAI reviewとAzure PR作成として分離する。

## 1. 目的

GitHub public repository の Pull Request に対して既存の決定的な `verify` が成功した後、Azure 上で AI review と必要な修正提案を実行する。AI の結果と修正は Azure Repos の private Pull Request に集約し、人間が承認して merge した変更だけを GitHub public repository へ反映する。

```text
GitHub public PR
  → GitHub Actions verify
  → 成功時だけ Azure Service Bus へ通知
  → Azure Repos に対象 branch / PR を作成
  → AI review / 修正提案
  → 人間が Azure Repos PR をレビューして merge
  → 最終 verify と競合確認
  → GitHub public main へ force なしで反映
```

## 2. 確定している設計原則

- GitHub public `main` を最終的な Source of Truth とする。
- Azure Repos は review と人間承認のための private staging area とする。
- GitHub Actions の `verify` 成功を AI review の開始条件にする。
- GitHub public `main` は Azure Repos の review 対象 base branch へ fast-forward で同期する。
- PR head は対象 Pull Request の処理時にだけ Azure Repos の限定 branch へ取り込む。repository 全体の常時 mirror はしない。
- AI process に GitHub/Azure Repos の write credential、merge 権限、policy bypass 権限を渡さない。
- GitHub Actions と Azure の間は OIDC workload identity federation を使用し、長期 client secret を置かない。
- GitHub へ反映するときは force push を使用しない。想定 base から進んでいたら停止する。
- Git commit SHA は対象 commit、重複、stale result、競合の識別に使う。信頼の暗号学的証明には使わない。

## 3. 公開情報とprivate情報の境界

public repository に置くもの:

- 全体アーキテクチャと権限分離の原則
- GitHub Actions workflow
- GitHubからAzureへ通知する項目の意味
- 実装フェーズ、承認ゲート、完了条件

private infrastructure repository に置くもの:

- Azure subscription、tenant、client、principal の各ID
- Azure DevOps organization、project、repository のID
- resource group、Service Bus、Managed Identity等の具体名
- Bicep parameter、deployment output、what-if/read-back記録
- private repository の commit、Pull Request、branch policy の運用記録

ID自体がcredentialでなくても、公開側で運用構成を再構成できる情報は重複して記録しない。GitHub Actionsには具体値をrepository secretsから渡し、workflow fileへ埋め込まない。

## 4. 現在実装しているGitHub dispatch

`.github/workflows/ai-review-dispatch.yml` はdefault branchにある固定コードとして、`verify` workflowの完了を`workflow_run`で受け取る。

実行条件:

- kill switch が明示的に有効
- `verify` の結論が `success`
- 対象がこのpublic repository
- eventがPull Request、または`main`へのpush

処理内容:

1. job単位の`GITHUB_TOKEN`でGitHub APIからworkflow runを再取得する。
2. Pull Requestの場合は、成功runのhead SHAに関連するopen Pull RequestをGitHub APIで一意に解決する。
3. Pull Request APIからbase/headを再取得し、repository、workflow名、workflow file、event、success、head SHAの対応を`jq`で検証する。
4. GitHub OIDC JWTをMicrosoft Entraの短期access tokenへ交換する。
5. queue-scopeのSender権限でService Busへ通知する。

privileged workflowは次を行わない。

- Pull Request codeのcheckoutまたは実行
- 先行workflowのartifact/cacheの読み込み
- Pull Request title/bodyのshell展開
- Azure management APIの呼び出し
- Azure ReposまたはGitHubへのwrite

## 5. 現段階のmessage contract

producerはpublic workflow、consumerはprivate infrastructure repositoryのGo workerとして実装済みである。
物理JSON Schemaと独立validatorは持たず、messageの実体はworkflow内の固定`jq`式とconsumerのnative
typeを正とする。

### `base-sync-request`

GitHub public `main`の`verify`成功後に送る。

- schema version
- event type
- 発生時刻
- GitHub repository identity
- workflow run ID / attempt / event / conclusion
- source ref
- verified commit SHA

### `review-request`

GitHub public Pull Requestの`verify`成功後に送る。

- schema version
- event type
- 発生時刻
- GitHub repository identity
- Pull Request番号
- workflow run ID / attempt / event / conclusion
- base ref / SHA
- head ref / SHA
- 成功したworkflow runに結び付いたhead SHA

この2種類だけをconsumerのnative typeとして定義し、実際のService Bus messageでlive integrationを
確認済みである。JSON Schemaは、異なる言語・repository間でnative typeだけではdriftを検出できないと
確認した場合に追加する。

`review-result`と`promotion-request`はproducerもconsumerも未実装なので、現時点では物理contractを固定しない。

## 6. AI provider adapterと未信頼出力検証

最初に実接続するproviderはClaudeとKiroの2つに決定した。Claudeは非対話のMessages API、KiroはCLIの
headless modeを使う。いずれもsource treeのread/searchだけを許可し、write、任意shell実行、network拡張、
Azure Repos操作を許可しない。ClaudeはAzure Managed IdentityをAnthropic Workload Identity Federationへ
接続する構成を第一候補とし、Kiroは公式headless modeが要求するAPI keyをsecret storeから実行時だけ渡す。

CodexとOpenCodeは今回の実装対象に含めず、実接続時までinterfaceを固定しない。

したがって現在はadapterを置かない。最初のproviderを実接続するときに次を同時に実装する。

- Claude/Kiroの実provider responseを受けるprovider別adapter
- provider固有形式から最小共通findingへの変換
- malformed、oversize、path traversal、存在しないpath/lineの拒否
- credentialや任意commandをcontrollerへ渡さない境界
- 実responseを匿名化したfixtureとadapter unit test

この検証は「設定作業を守るため」ではなく、AIが毎回返す未信頼データを副作用へ渡さないために必要である。必要になるのはprovider接続時であり、現在ではない。

## 7. Machine-to-machine認証

GitHub ActionsからAzureへの認証は次の境界を維持する。

```text
job用GITHUB_TOKEN
  → GitHub APIのreadだけ

GitHub OIDC JWT
  → Microsoft Entraのtoken endpointだけ

Service Bus向けEntra access token
  → 対象queueへのsendだけ
```

- tenant ID、managed identity client ID、namespace、queueはGitHub repository secretsから渡す。
- client secret、PAT、publish profile、SAS keyをGitHubへ保存しない。
- Federated Identity Credentialはpublic repositoryとdefault branchに限定する。
- dispatch identityには対象queueの`Azure Service Bus Data Sender`だけを割り当てる。
- Service Busのlocal/SAS authenticationは無効にする。
- tokenをmessage、artifact、log、永続fileへ含めない。

## 8. 実装フェーズ

| Phase | 内容 | 状態 |
| --- | --- | --- |
| 0 | 目的、Source of Truth、人間承認、権限境界の合意 | 完了 |
| 1 | public/private情報境界と最小message項目の合意 | 完了 |
| 2 | GitHub `workflow_run` dispatch、OIDC、Service Bus Sender基盤 | 完了 |
| 3 | Service Bus consumerとstate、実messageのintegration test | 完了。live dispatch、Job成功、保存ログ、queue/DLQ空を確認 |
| 4 | GitHub `main`からAzure mapped baseへのfast-forward同期 | 完了。Managed Identity権限、Job適用、branch新規作成、SHA一致、queue/DLQ空を確認 |
| 5A | Pull Request headの限定branch import | private実装中。provider credentialやAzure PR権限は不要 |
| 5B | Claude/Kiro adapter、AI review、Azure PR作成 | provider決定済み。実装・credential設定前 |
| 6 | AI fix proposal、credential-less verify、Azure人間承認 | 未着手 |
| 7 | Azure人間merge後のGitHub promotion | 未着手 |
| 8 | shadow rollout、監視、DLQ/reconciliation、費用上限 | 未着手 |

## 9. 自動テストを追加する基準

自動テストはfile数や設定の希少性ではなく、実行頻度と障害時の影響で判断する。

現在残す検査:

- `actionlint`: workflow構文、式、権限指定、shell連携
- `shellcheck`: workflow内shellの静的検査
- repository共通の`gitleaks`: credential混入検査
- `scripts/verify.sh`: 既存品質ゲートへの非干渉確認

Phase 2時点ではconsumerが未実装でworkflow変更頻度も低かったため、実際の過去workflow runをGitHub APIから読み、`pull_requests`が空でもhead SHAからPull Requestを解決できることを変更時に手動確認した。この確認を模倣する専用mock testは常設しない。

現在は追加しない検査:

- 現行consumer用の独立JSON Schema fixture test（workflowの固定`jq`とGo native typeで境界を検証するため未追加）
- 未選定providerのfake adapter test
- 将来のreview/promotion state machineを模倣するunit test
- workflow本文を文字列検索するだけの専用Python test

Phase 3/4で実施した検査:

- consumer: `go vet`、`go build`、digest固定image build、実messageによるlive integration
- Git同期: 恒久test fileは追加せず、一時bare repositoryでbranch作成、fast-forward、no-op、stale、
  分岐停止を手動確認

将来追加する検査:

- provider接続時: 実adapterのmalformed/oversize/path/line test
- promotion実装時: 人間承認対象SHA、policy、expected base、force禁止のtest

## 10. 未決事項と承認ゲート

- Claude/Kiroのmodel、1 review当たりのbudget・timeout上限
- Azure Repos branch policyと人間承認の実測方法
- GitHub Publisher Appとmain rulesetの最小権限構成
- retention、監視、通知、DLQ/reconciliationの具体値

これらは対象Phaseの実装直前に、実際のservice/API仕様を基に決める。先行してSchema、fake、数値閾値を固定しない。

## 11. Phase 2の確認済み事項

private IaC repositoryで次を適用し、read-backを確認済みである。

- Service Bus namespaceとdispatch queue
- GitHub Actions用user-assigned managed identity
- public repositoryのdefault branchに限定したFederated Identity Credential
- queue-scopeの`Azure Service Bus Data Sender`
- local/SAS authentication無効化

GitHub repository secrets 4件とkill switch用repository variableは設定済みである。Phase 3の実接続確認後、
kill switchは`true`へ変更した。具体的なID、resource名、deployment結果はprivate infrastructure
repositoryだけで管理する。

## 12. Phase 2の完了条件

- public workflowのPull Requestがレビューされている。
- `actionlint`、`shellcheck`、`scripts/verify.sh`が成功する。
- public差分にAzureの具体的なID・resource名・credentialが含まれない。
- workflowがPR code、artifact、cacheをprivileged jobへ取り込まない。
- 当時、kill switchを有効化していない。
- 当時、実consumerとlive sendをPhase 3の別承認事項として残していた。

この節はPhase 2完了時点のhistorical gateであり、Phase 3の別承認・実接続確認は完了している。

## 13. Phase 3/4完了とPhase 5開始位置

Phase 3ではprivate infrastructure repositoryのGo consumer、ACR、Table Storage、Container Apps Jobを
適用した。成功済みGitHub `main` verifyを1回だけ再実行し、OIDC dispatch、Service Bus受信、Job実行、
Table保存ログ、queue/DLQが空になるところまで確認した。

Phase 4は次の境界で実装・適用した。

- 同期元はGitHub public `main`、同期先はAzure Reposの固定mapped base branch。
- messageの検証済みSHAが現在のGitHub `main`履歴にあることを再確認する。
- branch未作成、fast-forward、同一SHA、順序逆転を区別する。
- Azure側だけのcommitまたは分岐を検出したら停止し、force pushしない。
- Azure DevOps向け短期tokenはManaged Identityで取得し、fileやlogへ保存しない。

controller Managed Identityのorganization登録と対象repositoryの最小権限を人間が確認し、image更新とJob適用後、
成功済みGitHub `main` verifyを再実行した。Azure mapped base branchの新規作成、GitHub `main`とのSHA一致、
Job成功、queue/DLQ空を確認した。

## 14. Phase 5A Pull Request head import

Phase 5Aは既存controller identityの権限内で次だけを行い、Azure Pull Request作成やAI provider呼出しを
含めない。

- GitHub取得元は、検証済みPull Request番号から作る`refs/pull/<number>/head`に固定する。
- messageのhead branch名は表示用metadataに留め、Git refやcommandを生成しない。
- 現在のGitHub Pull Request headが検証済みhead SHAと違う場合はstaleとして何も作らない。
- 現在のGitHub `main`とAzure mapped baseが、どちらもrequestのbase SHAと一致するときだけ続行する。
- Azure取込み先は`github-pr/<number>/<full-head-sha>`とし、同じGitHub headごとの限定branchにする。
- branch未作成なら通常push、同一SHAならno-op、異なるSHAがあれば停止する。force pushは使用しない。
- push後にremote refを読み戻し、検証済みhead SHAとの完全一致を確認してからmessageを完了する。

Phase 5BではClaude/Kiroをcontrollerとは別のone-shot Jobで実行する。AI JobのManaged IdentityはAzure Repos
Readだけを持ち、write、PR操作、merge、policy bypassを持たない。provider出力はcontrollerへ渡す前に
malformed、oversize、path traversal、存在しないpath/lineを拒否する。fake adapterや未選定provider用の
共通化は追加しない。
