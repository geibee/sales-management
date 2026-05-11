# Ralph Orchestrator

DAG ベースの multi-session RALPH。プロジェクト直下の `.ralph/tasks.toml` からタスクを順次拾って `claude -p` サブプロセスを並列起動し、verify 緑なら main に自動マージする。

## ralph-loop との違い

| | `/ralph-loop` (sibling plugin) | `/ralph-orch` (this plugin) |
|---|---|---|
| ループ単位 | 1 セッション内で同じプロンプト再投入 | DAG 内のタスクをサブプロセス並列実行 |
| 並列性 | なし (単一 Claude) | あり (worker pool, デフォルト 3) |
| タスク定義 | 1 個のフリーテキスト prompt | tasks.toml (依存・並列性・verify 付き) |
| 完了判定 | `<promise>X</promise>` 文字列マッチ | `<task-status>done</task-status>` + verify script 終了コード 0 |
| マージ | なし (同じ worktree) | git worktree → main rebase merge auto-push |
| 想定用途 | 単一の自己改良タスク (例: テストが緑になるまで) | 複数フェーズのプロジェクト推進 |

両者は併用可能。

## クイックスタート

```bash
# プロジェクトルートで
/ralph-orch dry-run                      # DAG と次に走るタスクを表示するだけ
/ralph-orch start                        # 自走開始 (バックグラウンド)
/ralph-orch status                       # 現在の状況
/ralph-orch logs P2-B                    # 走行中タスクの子プロセスログ
/ralph-orch resume                       # halt 状態から続き
/ralph-orch stop                         # 全走行タスクを停止
/ralph-orch run P2-B                     # 単一タスクのみ実行 (デバッグ用)
/ralph-orch lint                         # tasks.toml 整合チェック
```

## プロジェクト要件

このプラグインを使うプロジェクトは以下を持っていること:

```
<project-root>/
├── .ralph/
│   ├── tasks.toml                    # タスク DAG (commit 対象)
│   └── verify/<task-id>.sh           # タスク固有 verify (commit 対象)
├── .gitignore                        # .ralph/state.json と .ralph/logs/ を除外
└── (任意) CLAUDE.md                  # ralph-worker が読み込むプロジェクト指針
```

`tasks.toml` の最小例:

```toml
[meta]
schema_version = 1
default_model = "opus"
worker_pool_size = 3
worktree_prefix = "../mr-ralph-"        # worktree 配置先のプレフィックス
branch_prefix = "ralph/"                # 作業ブランチのプレフィックス
auto_merge = true                       # verify 緑なら main に rebase merge
auto_push = true                        # マージ後 origin に push

[[tasks]]
id = "P2-B"
title = "Sandbox: eval time/memory budget"
phase = "Phase2"
size = "2-3S"
files = ["lib/expression/eval.mbt", "lib/expression/eval_test.mbt"]
parallel_with = ["P2-C-1"]
serial_only = false
depends_on = []
verify = ".ralph/verify/P2-B.sh"
prompt_extra = """
具体的予算: 100ms / 1000 step / 4MB ヒープ。
追加テスト 4 本 (タイムアウト / step 上限 / nest 上限 / 正常 fast-path)。
"""

[[tasks]]
id = "R1-A"
phase = "R1"
serial_only = true
halt_before = true                      # このタスク開始前で必ず halt
depends_on = ["P2-B"]
verify = ".ralph/verify/R1-A.sh"
```

## 動作モデル

```
1. /ralph-orch start
2. orchestrator.sh をバックグラウンド起動 (.ralph/orchestrator.pid を作成)
3. メインループ:
   a. tasks.toml を load
   b. ready キューを計算 (deps 満たす ∧ skip:false ∧ serial 競合なし)
   c. キュー先頭が halt_before:true なら → drain → 終了 (人間レビュー待ち)
   d. worker_pool_size まで worker をスポーン
   e. spawn 内容:
       - git worktree add <prefix><id> -b <branch_prefix><id> main
      - claude -p \
          --model <default_model> \
          --append-system-prompt "$(render prompts/task-prompt.tmpl.md)" \
          --settings ".claude/settings.local.json" \
          --allowed-tools "..." \
          --output-format stream-json \
          < /dev/null
   f. 子プロセスが <task-status>done|blocked: ...</task-status> を吐いたら捕捉
   g. verify script 実行
   h. ◯: rebase main → fast-forward merge → push → state.json 更新 → worktree 撤去
      ✗: state.json に blocked 記録、worktree は残してデバッグ用
4. R-frame に到達したら halt し終了
```

## Skill 配信

ralph-worker subagent は spawn 後に下記 Skill を `Skill()` で呼べる:

- このプラグイン同梱: `ralph-task` (タスクライフサイクル契約の再確認)
- ユーザ環境にあるもの: `moonbit-agent-guide`, `moonbit-refactoring`, `simplify`, `review` など `~/.claude/skills/` にあるもの全て

タスク固有のヒントは `tasks.toml` の `prompt_extra` に書く。

## 安全装置

- **R-frame halt**: `halt_before = true` のタスク直前で必ず停止
- **worktree 隔離**: 各 worker は別 worktree。並列でも干渉しない
- **serial_only**: これが true のタスクが ready の間は他 worker 起動を抑止
- **verify 失敗時はマージしない**: blocked 状態で残す
- **auto_push = false** にすればローカル commit 止まり (人間が後で push)
- **`/ralph-orch stop`**: 走行中の worker プロセスを SIGTERM、worktree は残す

## 制約

- POSIX bash + jq + git + claude CLI が必要 (`tomlq` は使わず内蔵パーサで対応)
- 子 Claude は `--dangerously-skip-permissions` を **使わない**。プロジェクト側 `.claude/settings.local.json` の allowlist + `--allowed-tools` で許可する
- API コスト上限なし。ログで事後計測のみ
