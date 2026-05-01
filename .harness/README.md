# `.harness/` — マルチエージェントオーケストレーション基盤

Phase 2 Step 29 の成果物。`prd.md` のタスクを 4 専門エージェントに分配する仕組み。

## 構造

```
.harness/
├── agents/              # 各エージェントの責務 / 禁止 / I/O を定義した JSON
│   ├── domain-modeler.json   # F# 型定義のみ生成
│   ├── test-writer.json      # FsCheck PBT / ArchUnit / Pact テスト記述
│   ├── refactorer.json       # merged.sarif の警告解消
│   └── doc-updater.json      # ドキュメント整合
├── inbox/<agent>/       # master.py が書き込む入力メッセージ <task-id>.json
├── outbox/<agent>/      # 各エージェントが結果を書き戻す <task-id>.json
├── lessons.md           # エージェント間の手書き共有メモ (短期記憶)
└── master.py            # オーケストレーター
```

## 使い方

```bash
# 1 件だけ dry-run (inbox 書き込みのみ、Claude CLI 起動なし)
python3 .harness/master.py --prd prd.md --dry-run

# 実起動 (claude code CLI が PATH にある前提)
python3 .harness/master.py --prd prd.md

# 複数タスク
python3 .harness/master.py --prd prd.md --max-tasks 5
```

## メッセージ仕様

### inbox/<agent>/<task-id>.json

```json
{ "task": "<PRD から抜き出したタスク本文>", "task_id": "<8-hex>" }
```

### outbox/<agent>/<task-id>.json (各エージェントが書く)

```json
{
  "status": "ok | error",
  "files": ["apps/api-fsharp/src/SalesManagement/Domain/Types.fs", ...],
  "notes": "..."
}
```

## エージェント追加手順

1. `.harness/agents/<new-agent>.json` を作る (既存ファイルのスキーマに従う)
2. `.harness/inbox/<new-agent>/.gitkeep`, `.harness/outbox/<new-agent>/.gitkeep` を追加
3. `.harness/master.py` の `AGENT_PIPELINE` リストに名前を追加 (順序が重要)

## Step 30 (RALPH ループ) との関係

`harness/ralph.sh` (Step 30 で追加) は while ループで本スクリプトを呼ぶ薄いランナー。1 反復 = 1 タスク = 1 master.py 実行。CI 緑なら `prd.md` の `[ ]` を `[x]` に書き換える責務は ralph.sh 側に置く (master.py は単発実行に専念)。
