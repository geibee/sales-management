# 改修依頼書 — GitHub public PRのAzure AIレビュー

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| トラック | **A: 新しい権限・非同期状態・人間承認を追加するため** |
| 状態 | approved / Phase 5A・5B complete / Phase 6設計承認待ち |
| 承認日 | 2026-07-19 |

本書は`specs/README.md`で全依頼に必須とされる合意記録である。実装完了後は凍結するため、runtime contractや運用値の恒久的なSource of Truthにはしない。

## 1. 目的

GitHub public Pull Requestの既存`verify`が成功した後にAzure上でAI reviewと修正提案を行う。結果はAzure Reposのprivate Pull Requestで人間が確認し、人間がmergeした変更だけをGitHub public `main`へ反映する。

## 2. スコープ

含むもの:

- `verify`成功後の非同期dispatch
- GitHub public `main`のAzure Reposへのfast-forward同期
- GitHub Pull Request対象commitの限定branchへの取込み
- AI reviewと修正案をAzure Repos Pull Requestへ集約する処理
- 人間merge後の最終verify、競合確認、GitHubへのforceなし反映
- OIDC、Managed Identity、GitHub Appによるmachine-to-machine認証

含まないもの:

- GitHub repository全体の常時mirror
- `verify`失敗・取消・skip時のAI review
- AI、worker、controllerによる自己承認またはmerge
- force push、branch policy bypass、個人PAT/SSH credential
- 実provider未選定段階のadapter、response Schema、state machine実装

## 3. 受入基準

| ID | Given | When | Then |
| --- | --- | --- | --- |
| AAR-AC-01 | GitHub Pull Requestの`verify`が成功 | dispatchが実行される | 対象repository、Pull Request、SHAを再検証したrequestだけをAzureへ通知する |
| AAR-AC-02 | `verify`がfailure、cancelled、skipped | workflowが完了 | Azureへの通知とAI reviewを開始しない |
| AAR-AC-03 | Azure Repos上の修正を人間が承認・merge | promotionを実行 | 最終verifyとexpected baseが一致するときだけforceなしでGitHubへ反映する |
| AAR-AC-04 | GitHub `main`がexpected baseから進行 | promotionを実行 | 自動mergeまたはforce pushをせず停止する |
| AAR-AC-05 | 同一requestが重複または順序逆転 | controllerが処理 | 同じ副作用を重複させず、base未到達なら待機する |

## 4. 権限と信頼境界

- GitHub Actions dispatchはGitHub API readとService Bus queue sendだけを持つ。
- privileged dispatchはPull Request code、artifact、cache、title/bodyを実行入力にしない。
- AI review stepはGitHub/Azure Reposのcredentialを持たない。
- Azure Repos Pull Request作成は、AI processと分離したtrusted Pipeline stepが担当する。
- AI outputは未信頼のMarkdown文字列として扱い、実行、Git操作、vote、自動merge条件へ使用しない。
- public repositoryにはAzureの具体的なsubscription/tenant/client/principal/project/repository IDやresource名を記録しない。

## 5. 確定事項

- 最終Source of TruthはGitHub public `main`。
- 同期対象base branchは当初`main`だけ。
- Azure Reposで最低1名の人間が承認する。requestor、AI、controllerの自己承認は禁止。
- Azure Reposで人間mergeした後、GitHubへ直接pushする。ただしforceは使用しない。
- external resource変更はPhaseごとにplan、費用、rollbackを提示して個別承認を得る。
- GitHub ActionsからAzureへはOIDC workload identity federationを使用する。
- dispatchのkill switchは実consumer完成とlive sendの別承認まで無効にする。2026-07-21に承認後有効化し、
  Phase 5Bのlive確認完了後は開発中の追加課金を避けるため再び無効化した。

## 6. 現段階で固定しないもの

- 現行producer/consumer間の独立JSON Schema（workflowの固定`jq`とconsumerのGo native typeを正とする）
- Phase 5B用の`review-work`、`review-result-ready`、provider結果Schema（Azure branch更新を直接triggerにするため作らない）
- `promotion-request`のfield構成
- Codex、OpenCodeのprovider adapter interface
- retry、retention、監視間隔等の具体値
- Container Apps Job、state store、controllerの最終構成

これらは実装対象Phaseの開始時に、実際のAPI・SDK・障害モデルを確認して仕様化する。

## 7. 未決事項

| ID | 論点 | 決定時期 |
| --- | --- | --- |
| Q-01 | controller用Managed Identityをorganizationへ明示登録し、対象repositoryだけに最小権限を付与する | 完了。Phase 4適用前に人間が登録・権限確認 |
| Q-02 | 最初に接続するAI provider | 完了。ClaudeとKiroを選定 |
| Q-03 | Publisher GitHub Appとmain rulesetの構成 | promotion実装前 |
| Q-04 | retention、通知、DLQ/reconciliationの具体値 | shadow rollout前 |

## 8. 変更履歴

| 日付 | 内容 |
| --- | --- |
| 2026-07-19 | 全体方針、Source of Truth、人間承認、force禁止を承認 |
| 2026-07-20 | Phase 2 Azure dispatch基盤をprivate IaCで適用・read-back |
| 2026-07-20 | 将来Schema、fake adapter、予測テストを削除し、実装時追加へ変更。public/private情報境界を明文化 |
| 2026-07-21 | Phase 3 consumerを適用し、GitHubからTable保存までlive integrationを確認。Phase 4のcontroller identity方針を確定 |
| 2026-07-21 | Phase 4を適用し、GitHub `main`からAzure Repos mapped baseへの実同期、SHA一致、queue/DLQ空を確認 |
| 2026-07-21 | 非同期・非機能仕様をPhase 4完了状態へ同期し、Phase 5以降の未実装範囲と分離 |
| 2026-07-21 | Phase 5をPR head importの5AとAI review/Azure PR作成の5Bに分離し、ClaudeとKiroを選定 |
| 2026-07-21 | Phase 5Bの読取専用AI Job、provider結果state、PR controller間の最小contractを起案 |
| 2026-07-21 | 前項の過剰な内部contractを撤回。trusted Azure Pipelineがbranch更新を直接受け、Claude/Kiro review後に固定stepでAzure PRを作る方式を承認 |
| 2026-07-21 | default Claude reviewからAzure Repos Pull Request作成後read-backまでlive確認し、Phase 5Bを完了。開発中はkill switchを無効化 |

## 品質ゲート化対応表

| 仕様項目 | 現在のゲート | 将来のゲート |
| --- | --- | --- |
| AAR-AC-01/02 | `actionlint`、`shellcheck`、workflow review、live dispatch integration | Azure PipelineからPR作成までのend-to-end確認 |
| AAR-AC-03/04 | 未実装 | promotion実装時のGit E2Eと権限negative test |
| AAR-AC-05 | base同期とPR head取込みのduplicate/reorder/staleを実装・手動Gitシナリオ確認済み | Azure PRの同一source/target再利用をlive確認 |
| credential混入 | repository共通`gitleaks`、image/logとtoken受渡しのreview | provider secretを各AI step、`System.AccessToken`をPR作成stepだけに渡すことを確認 |
