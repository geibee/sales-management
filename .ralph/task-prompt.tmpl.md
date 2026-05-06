# RALPH Task: {{ id }} — {{ title }}

You are a ralph-worker subagent. Your sole job is to complete this single task.

> **重要**: この task spec の検証コマンドが正本です。前段に貼られた worker-contract.md は MoonBit プロジェクト向けに書かれており、`moon check` / `moon test` / `moon info` / `moon fmt` への言及がありますが、**本リポジトリは F# / .NET なので無視してください**。下記「完了条件」の dotnet コマンドだけ実行すればよい。`Skill(moonbit-agent-guide)` も呼ばないでください（本リポジトリには関連スキルなし）。

## Task

- **ID**: {{ id }}
- **Phase**: {{ phase }}
- **Size**: {{ size }}

## 編集対象ファイル (このリスト以外は read-only)

{{ files }}

## 完了条件 (programmatic — すべて通って初めて done)

1. `dotnet build apps/api-fsharp` 終了コード 0
2. `dotnet test apps/api-fsharp/tests/SalesManagement.Tests --filter Category=Integration` 終了コード 0、かつ pass テスト数 ≥ {{ baseline_test_count }}
3. `bash {{ verify }}` 終了コード 0 (タスク固有 verify — これが最終ゲート)

## タスク固有の指示

{{ prompt_extra }}

## 進め方

1. 起動直後: `Skill(ralph-task)` を呼んでライフサイクル契約を確認
2. `git status` (worktree clean のはず) と現在のブランチを確認
3. `~/.claude/plans/pbt-groovy-sparkle.md` の該当 Stage と編集対象ファイルを Read
4. 実装 → 上記 3 段検証 → green
5. `git add <編集ファイル>` + `git commit -m "..."` (Co-Authored-By なし、日本語コミットメッセージ)
6. 最終ターン末尾に `<task-status>done</task-status>` を出力して終了

## F# / .NET 固有の注意

- **Postgres コンテナが必要**: 統合テストは Testcontainers で Postgres を起動する。Docker daemon が動いている前提で進める
- **fsproj の Compile 順**: F# は宣言順依存。`Support/*` を追加するときは `<Compile Include>` の先頭に置く
- **テスト Trait**: 新規テストには `[<Trait("Category", "Integration")>]` を付ける。S1/S2 では `Smoke` / `Param` を新設する場合あり（タスク固有指示参照）
- **JSON シリアライズ**: 既存テストは `System.Text.Json` を使用。フィールド命名は camelCase

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
- `files:` 外のファイル編集（特に `~/.claude/plans/pbt-groovy-sparkle.md` / `.ralph/*` / `prd.md` / `AGENTS.md` の自動生成領域）
- `git push` / PR 作成
- `--no-verify` などの hook bypass
- 不要なリファクタリング・既存テストの "ついでに修正"

参考: `Skill(ralph-task)`、親プラン `~/.claude/plans/pbt-groovy-sparkle.md`
