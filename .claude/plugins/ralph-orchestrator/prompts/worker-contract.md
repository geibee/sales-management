# Worker Contract (canonical reference)

`agents/ralph-worker.md` と `skills/ralph-task/SKILL.md` は本ファイルの内容を要約・分配したもの。三者で食い違いが出た場合の正本はここ。

## 1. スコープ

- 1 task = 1 worker = 1 worktree = 1 branch = 1 commit (理想)
- worktree 内のみ書き込み権限を実質的に持つ。orchestrator は他 worktree も操作する
- system prompt の `files:` リストが編集スコープの権威。ただしテストファイル新規作成は隣接配置で許可

## 2. 検証

```
moon check        → exit 0
moon test         → exit 0, passed >= baseline
moon info         → exit 0
moon fmt          → exit 0 (no diff after fmt が望ましい)
bash <verify>     → exit 0
```

## 3. 完了マーカ

stream-json log 内のアシスタントメッセージから orchestrator が抽出するため、

- `<task-status>done</task-status>` (成功)
- `<task-status>blocked: ...</task-status>` (中断)

の **どちらか一方** が最終出力に含まれる必要がある。両方ある場合は最初の出現を採用。

## 4. コミット

- 1 task 1 commit
- `<type>({phase|lower}): {title}` 形式
- Co-Authored-By なし

## 5. 並列性

- 隣 worktree で別 worker が動いていても干渉しない (worktree 隔離)
- 共有ファイル (例: pkg.generated.mbti) を `moon info` で再生成すると競合の可能性あり → orchestrator は serial_only タスクで対処、worker 側は気にしない

## 6. 失敗のエスカレーション

- 3 回 retry して verify 緑にならない → blocked
- 設計判断が要る (例: API 形状、命名) → 推測せず blocked、必要な意思決定を明示

## 7. 禁止リスト (強制)

| カテゴリ | 行為 |
|---|---|
| Git | push, PR 作成, --force, --no-verify |
| FS | files: 外編集, .ralph/* 編集, PLAN.md 編集 |
| Net | Web fetch, npm install (allowlist 外), curl 外部 |
| 嘘 | verify 通っていない状態で done を出力 |
