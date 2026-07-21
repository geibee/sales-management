# 非機能要件 — GitHub / Azure AIレビュー

## メタ情報

| 項目 | 記入 |
| --- | --- |
| 依頼ID | `CR-20260719-azure-ai-review` |
| 対象 | 認証、権限分離、既存verifyへの非干渉、公開情報 |
| 状態 | approved / Phase 4 complete |

## 1. 現段階で必須の要求

| ID | 要求 |
| --- | --- |
| AAR-NFR-01 | Azure、Azure DevOps、AI providerが停止しても既存GitHub `verify`の判定を変えない |
| AAR-NFR-02 | GitHub ActionsからAzureへはOIDC workload identity federationを使い、長期client secret、PAT、SAS keyを置かない |
| AAR-NFR-03 | dispatch identityは対象Service Bus queueへのsendだけを持ち、receive、Azure resource管理、Azure Repos操作を持たない |
| AAR-NFR-04 | privileged workflowはPull Request code、artifact、cache、title/bodyを実行入力にしない |
| AAR-NFR-05 | token、private key、provider credentialをmessage、artifact、log、永続fileへ記録しない |
| AAR-NFR-06 | public repositoryへAzureの具体的なsubscription/tenant/client/principal/project/repository ID、resource名、deployment outputを記録しない |
| AAR-NFR-07 | kill switchは実consumer完成とlive send承認まで無効にする。2026-07-21に承認後、有効化済み |

## 2. Phase 5以降のcomponent実装時の要求

| 対象 | 要求 |
| --- | --- |
| Azure controller | controllerごとにManaged Identityとrepository/branch権限を分離し、tokenをmodel processへ渡さない |
| AI worker | one-shotで実行し、GitHub/Azure Repos write、merge、policy bypassを持たない |
| provider output | malformed、oversize、path traversal、存在しないpath/line、任意commandを拒否してから副作用へ渡す |
| Azure人間承認 | requestor、AI、controllerの自己承認を禁止し、承認対象headとmerge対象headを再検証する |
| GitHub promotion | repositoryを限定した短期GitHub App installation tokenを使い、expected base不一致とforceを拒否する |
| 運用 | retry、DLQ、reconciliation、retention、通知、費用上限をshadow rollout前に承認する |

## 3. 数値を今決めない理由

consumer、state store、Container Apps Job、base同期はPoC実装済みだが、AI provider、PR import、
review/promotion controller、継続運用は未実装である。初回実接続だけでは処理時間、retry回数、retention日数、
finding上限の最終値を決める根拠が不足するため、Phaseごとに実測可能になった時点で障害影響と費用を提示して決定する。

ただし次の安全条件は件数や負荷に依存しないため、先に固定する。

- credential漏えいを許容しない。
- AIまたはcontrollerの自己承認を許容しない。
- public historyのforce更新を許容しない。
- staleまたはbase conflict時に自動適用しない。

## 4. 現在の検証方法

| 要求 | 検証 |
| --- | --- |
| workflow構文・式 | `actionlint` |
| inline shell | `shellcheck` |
| credential混入 | repository共通`gitleaks` |
| 既存品質ゲートへの非干渉 | `scripts/verify.sh` |
| OIDC/FIC/RBAC/local auth | private IaCのlint、snapshot、Azure read-back |
| consumerとbase同期 | Go公式tool、digest固定image build、手動Gitシナリオ、live dispatchとSHA read-back |
| public情報境界 | Pull Request diffでAzure識別子・resource名を確認 |

専用Python test、fake provider、将来イベントSchemaは現段階の検証には使用しない。今後も、頻繁に実行される
未信頼入力境界または高影響の副作用を自動検証する便益が実装・保守コストを上回る場合に限りtestを追加する。
