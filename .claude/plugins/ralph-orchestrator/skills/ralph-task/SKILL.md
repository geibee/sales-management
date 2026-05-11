---
name: ralph-task
description: ralph-orchestrator から spawn された Claude (ralph-worker) が起動直後に呼び、本リポジトリ (F#/.NET) のタスクスコープ・検証フロー・コミット規約・完了マーカを再確認するためのプロジェクト固有契約
---

# Ralph Task Lifecycle Contract (F#/.NET 版)

ralph-orchestrator プラグインから spawn された ralph-worker が、長時間実行で契約を忘れたときに再確認するためのスキル。グローバルの `ralph-orchestrator:ralph-task` を本リポジトリ向けに翻案したもの。**矛盾した場合は本ファイルが優先**（言語固有コマンドが違うため）。

## あなたが今いる状況

- 親 orchestrator が `.ralph/tasks.toml` から 1 タスクを選び、専用 worktree (`<worktree_prefix><task-id>`、既定 `../mr-ralph-<id>`) を作って **そこ** にあなたを配置した
- ブランチは `<branch_prefix><task-id>`、main から派生
- セッション終了後に orchestrator が verify → main rebase merge → push を自動で行う
- 並列 worker が別 worktree で動いている可能性がある（自分の worktree 外を編集しない）

## 編集スコープ

- system prompt に列挙されている `files:` のパス**のみ** edit/write 可能
- それ以外は read-only
- 例外: 自分のタスクに必要なテストファイル新規作成は files に明示されていなくても OK だが、配置は `apps/api-fsharp/tests/SalesManagement.Tests/` 配下の隣接位置に限る

## 検証フロー (必須順、F#/.NET)

```bash
dotnet build apps/api-fsharp                                              # 型/コンパイル
dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --filter Category=Integration                                           # 全テスト緑
dotnet fantomas --check apps/api-fsharp/src apps/api-fsharp/tests         # フォーマット
bash <verify_script>                                                       # タスク固有検証 (system prompt に記載)
```

備考:
- 統合テストは Testcontainers で Postgres を起動する。**Docker daemon が動いている前提**
- F# は宣言順依存。`<Compile Include>` の順序に注意（Support 等は先頭）
- 新規テストには `[<Trait("Category", "Integration")>]` を付ける

verify_script が落ちたら絶対に done を出力しない。原因解析 → 修正 → 再 verify。3 回試して直らない場合は blocked。

## コミット規約

- メッセージ: `<type>({phase|lower}): {title}` 形式（例: `feat(s1): Support ハーネス導入`）
- 言語: **日本語**
- 1 タスク 1 コミット原則。WIP コミットは squash
- `Co-Authored-By` は **付けない**
- 編集対象でないファイルが `git status` に出ていたら止める（バグの兆候）

## 完了マーカ

最終アシスタントメッセージの末尾に正確に出力:

```
<task-status>done</task-status>
```

または:

```
<task-status>blocked: <理由>
試したこと: <X1>; <X2>; <X3>
必要な意思決定: <Y></task-status>
```

`<task-status>` タグは orchestrator が `worker.sh::worker_extract_status` で stream-json から拾う。タグがないと「no done marker」で blocked 扱い。

## やってはいけないこと

- `git push` / PR 作成 / 他 worktree 操作
- `.ralph/` 配下や `AGENTS.md` 自動生成領域の編集（orchestrator / Stop フック専有）
- `--no-verify` 等で pre-commit hook を skip
- 嘘の done（verify が走るので必ずバレるが API コストは発生する）
- 並列で動いている他 worker のブランチを merge/fetch（rebase は orchestrator が担当）
- `prd.md`（存在する場合）の編集

## 困ったときに使える Skill

- `Skill(simplify)` 完成前の最終レビュー
- `Skill(review)` PR 相当のセルフレビュー

## 本リポジトリ固有メモ

- 主要言語は日本語（コミット・コメント・ドキュメント）
- 識別子は英語（PascalCase 型 / camelCase 関数・フィールド）
- DSL 解釈ルールと命名規約は [`AGENTS.md`](../../../AGENTS.md) を参照
- CI: `apps/api-fsharp/ci.sh`（`ZAP_ENABLED=0` で DAST スキップして高速化可能）
- SARIF は `ci-results/sarif/<tool>.sarif` に出し `ci-results/merged.sarif` に統合する
