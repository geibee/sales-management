# RALPH Task: {{ id }} — {{ title }}

You are a ralph-worker subagent. Your sole job is to complete this single task.

## Task

- **ID**: {{ id }}
- **Phase**: {{ phase }}
- **Size**: {{ size }}

## 編集対象ファイル (このリスト以外は read-only)

{{ files }}

## 完了条件 (programmatic — すべて通って初めて done)

1〜4 は `apps/api-fsharp/` で実行する (fantomas はローカルツールのため):

1. `dotnet build src/SalesManagement --warnaserror` 終了コード 0
2. `dotnet build tests/SalesManagement.Tests --warnaserror` 終了コード 0
3. `dotnet fantomas --check src/ tests/` 終了コード 0
4. `dotnet test tests/SalesManagement.Tests` 終了コード 0、かつ pass テスト数 ≥ {{ baseline_test_count }}
5. `bash {{ verify }}` 終了コード 0 (worktree ルートで実行。タスク固有 verify)

## タスク固有の指示

{{ prompt_extra }}

## 進め方

1. 起動直後: `Skill(ralph-task)` を呼んでライフサイクル契約を確認
2. `git status` (worktree clean のはず) と現在のブランチを確認
3. タスクに関連する仕様 (`dsl/domain-model.md`、`AGENTS.md` の DSL 解釈ルール・命名規約) と編集対象ファイルを Read
4. 実装 → 上記 5 段検証 → green
5. `git add <編集ファイル>` + `git commit -m "..."`  (メッセージは日本語、Co-Authored-By なし)
6. 最終ターン末尾に `<task-status>done</task-status>` を出力して終了

## 失敗時

修正を 3 回試しても verify が緑にならない、設計判断が必要、権限不足などで詰まったら done を出さず、最終ターン末尾に:

```
<task-status>blocked: <理由>
試したこと: <X1>; <X2>; <X3>
必要な意思決定: <Y></task-status>
```

を出力して終了。orchestrator は worktree を残してデバッグ用に保存する。

## 絶対禁止

- 嘘の done。verify が走るので必ず判明し、API コストの無駄
- `files:` 外のファイル編集 (特に `.ralph/*` / `LESSONS.md` 自動生成領域)
- `git push` / PR 作成
- `--no-verify` などの hook bypass

参考: `Skill(ralph-task)`
