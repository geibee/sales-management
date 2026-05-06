# 開発スタイル

TDD で開発する（探索 → Red → Green → Refactoring）。
KPI やカバレッジ目標が与えられたら、達成するまで試行する。
不明瞭な指示は質問して明確にする。

# コード設計

- 関心の分離を保つ
- 状態とロジックを分離する
- 可読性と保守性を重視する
- コントラクト層（API/型）を厳密に定義し、実装層は再生成可能に保つ
- 静的検査可能なルールはプロンプトではなく、その環境の linter か ast-grep で記述する


# 言語

- 本リポジトリの主要言語は日本語。ドキュメント・コミットメッセージ・コメントは日本語で記述する
- ただしコード上の識別子（型名・関数名・変数名）は英訳して使う（命名規約セクション参照）

# PoC固有ルール

## ファイル配置規約

```
apps/api-fsharp/
├── src/SalesManagement/
│   ├── Domain/Types.fs          # DSLから生成した型定義
│   ├── Domain/Workflows.fs      # behaviorの実装（純粋関数）
│   ├── Infrastructure/          # DB永続化（Donald）
│   └── Api/                     # Giraffeルーティング
├── tests/SalesManagement.Tests/
└── tools/Migrator/

apps/api-kotlin/  (将来予定)
├── src/main/kotlin/salesmanagement/
│   ├── domain/Types.kt          # DSLから生成した型定義
│   ├── domain/Workflows.kt      # behaviorの実装（純粋関数）
│   ├── infrastructure/          # DB永続化（Exposed）
│   └── api/                     # Ktorルーティング
└── src/test/kotlin/salesmanagement/
```

## 命名規約

- DSLの日本語名を英訳してコードに使う
- 型名: PascalCase（例: `在庫ロット` → `InventoryLot`）
- フィールド/変数: camelCase（例: `製造完了日` → `manufacturingCompletedDate`）
- 関数名: camelCase、動詞始まり（例: `製造完了を指示する` → `completeManufacturing`）
- エラー型: `〜Error` suffix（例: `製造完了指示エラー` → `ManufacturingCompletionError`）

## DSL解釈ルール

- `data X = A OR B` → 判別共用体 / sealed interface
- `data X = A AND B` → レコード / data class
- `behavior F = Input -> Output OR Error` → 純粋関数（副作用なし）
- `List<X> // 1件以上` → NonEmptyList として扱う
- `フィールド?` → Option / nullable
- エラー型の内部構造はDSLに定義がないため、実装時に判断する

# RALPH ループによる後続開発

本リポジトリには 3 系統の自動化基盤がある：

| 基盤 | 用途 | 起動 |
|---|---|---|
| `.harness/` (master.py) | マルチエージェントオーケストレーション。`prd.md` をエージェント間で読み合う | `python3 .harness/master.py --prd prd.md` |
| `harness/ralph.sh` | **単一タスク** PRD ループ。`prd.md` の `[ ]` を 1 つずつ消化 | `bash harness/ralph.sh` |
| `.ralph/` (ralph-orchestrator) | **DAG ベース multi-session**。タスク間依存と halt_before レビュー点を持つ | `/ralph-orch start` |

`harness/ralph.sh` と `/ralph-orch` は別物（前者は単純 PRD ループ、後者は DAG）。混同しないこと。

`.ralph/` は親プラン `~/.claude/plans/pbt-groovy-sparkle.md`（Stage 1〜5 のテストハーネス充実）を Stage 6 として自走実行するためのもの。

- `.ralph/tasks.toml` — DAG 定義（git 管理）
- `.ralph/verify/*.sh` — 各タスクの検収スクリプト（git 管理）
- `.ralph/task-prompt.tmpl.md` — F#/.NET 用 worker prompt テンプレ（git 管理）
- `.ralph/capture-baseline.sh` — `dotnet test --list-tests` でテスト数計測（git 管理）
- `.ralph/state.json` / `.ralph/logs/` — ランタイム生成物（gitignore）

ralph-orchestrator プラグイン (`~/.claude/plugins/local/plugins/ralph-orchestrator/`) は MoonBit 前提で書かれており、F#/.NET 対応のため `scripts/lib/worker.sh` と `scripts/lib/verify.sh` に **`<repo>/.ralph/{task-prompt.tmpl.md, capture-baseline.sh, verify/_generic.sh}` があれば優先する 2〜3 行のフォールバック分岐** を入れてある。プラグイン更新時はパッチの再適用が必要。

worktree は `../mr-ralph-<task-id>` に隔離作成され、verify 緑+merge 後に削除、blocked 時は残置。累積したら `git worktree list` → `git worktree remove --force <path>` で掃除。

## 共通注意

後続のタスクを追加するときは以下を前提とする：

- すべての CI ツールの結果は `ci-results/sarif/<tool>.sarif` に出力し、`ci-results/merged.sarif` に統合する
- 失敗の自己分析は `Stop` フック（`.claude/scripts/sarif-to-lessons.py`）が本ファイル末尾の "## 失敗から学んだこと (自動生成)" セクションに追記する
- マルチエージェント間通信は `.harness/inbox/<agent>/` と `.harness/outbox/<agent>/` の JSON メッセージのみ（直接呼び出しは禁止）。詳細は `.harness/README.md` 参照
- RALPH ループの停止条件は以下のいずれか：
  - `prd.md` の全項目が `[x]` になる
  - `MAX_ITER`（デフォルト 20）到達
  - `BUDGET_USD`（デフォルト 10）到達

## 関連ディレクトリ

```
ci-results/                    # gitignore 対象
├── sarif/                     # ツール別 SARIF
│   ├── gitleaks.sarif
│   ├── trivy.sarif
│   ├── detekt.sarif
│   ├── fsharp-build.sarif
│   ├── sonar.sarif
│   └── zap.sarif
├── merged.sarif               # マージ済み（エージェント参照用）
├── sbom-fsharp.cdx.json       # SBOM
├── sbom-kotlin.cdx.json       # SBOM
└── renovate.log               # 依存更新候補

.claude/                       # Claude Code フック
├── settings.json
└── scripts/
    ├── start-trace.py         # SessionStart: trace ID 生成
    ├── emit-otel.py           # PostToolUse: OTel スパン送信
    └── sarif-to-lessons.py    # Stop: AGENTS.md 自動更新

.harness/                      # マルチエージェント基盤
├── agents/                    # サブエージェント定義
├── inbox/                     # 入力メッセージ
├── outbox/                    # 出力メッセージ
├── lessons.md                 # エージェント間共有メモリ
└── master.py                  # オーケストレーター

harness/                       # RALPH ループ
└── ralph.sh                   # green-loop runner

prd.md                         # PRD (SSoT、後続タスクの追記先)
progress.txt                   # RALPH 反復記録
```

## 起動手順

```bash
# 1 タスク dry-run（inbox 書き込みのみ）
python3 .harness/master.py --prd prd.md --dry-run

# RALPH ループを回す（CI 緑になるまで自己反復）
bash harness/ralph.sh
```

# スキル作成

新規 skill を作るとき、配置先を次の指針で決める:

- **project 固有** (`<repo>/.claude/skills/<skill-name>/SKILL.md`): 特定 repo のドメイン知識・規約・ファイルレイアウトに依存し、他 repo で使う見込みがない
- **グローバル** (`~/.claude/skills/<skill-name>/SKILL.md`): 言語・ツール横断、複数 repo で再利用可能、運用ノウハウ
- **判断不能なとき**: ユーザーに「project 固有かグローバルか」を質問してから作成（理由: 後から移動するとフロントマター参照や呼び出し側のスキル名が壊れやすい）

スキルファイルは YAML frontmatter（`name`, `description`）を先頭に記述する。Claude Code は `.claude/skills/` 配下を自動検出し、`Skill` ツール経由（または `/<skill-name>` スラッシュコマンド）で起動できる。サブエージェントを別途定義する場合は `.claude/agents/<agent-name>.md`（project）または `~/.claude/agents/<agent-name>.md`（user）に Markdown + frontmatter で配置する。

<!-- 以下は Stop フック (.claude/scripts/sarif-to-lessons.py) が自動追記する領域 -->

## 失敗から学んだこと (自動生成)
- 2026-05-01 OWASP ZAP.100000: 311回検出。A Client Error response code was returned by the server: 400
- 2026-05-01 OWASP ZAP.10049: 9回検出。Non-Storable Content: 400
- 2026-05-01 OWASP ZAP.10104: 5回検出。User Agent Fuzzer: <p>Check for differences in response based on fuzzed User Agent (eg. mobile sites, access as a Search
