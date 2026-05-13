# RALPH ハーネス改善計画

作成日: 2026-05-13

対象リポジトリ: `sales-management`

対象範囲:

- `.claude/plugins/ralph-orchestrator/`
- `.claude/scripts/`
- `.ralph/` 配下に今後追加するタスク定義、検証スクリプト、評価データ
- `AGENTS.md` とリポジトリ内の agent 向け知識ベース
- `apps/api-fsharp/ci.sh` と SARIF/coverage/trace 出力

## 目的

この計画は、現在の RALPH ループを「LLM による自動コード生成を自己改善するためのハーネス」として強化するための実装計画である。

狙いは単に agent に長く走らせることではない。次の性質を持つ開発ループへ段階的に移行する。

1. 検証が嘘を許さない
2. 失敗が構造化データとして残る
3. 候補生成と選別を実行時にスケールできる
4. agent が必要なコンテキスト、ログ、メトリクス、制約をリポジトリ内から読める
5. 人間の判断は曖昧な仕様決定とリスク判断に集中し、反復的な修正、レビュー、検証、記録はハーネスが担う

## 背景

本リポジトリには `.claude/plugins/ralph-orchestrator/` が同梱されている。これは `.ralph/tasks.toml` の DAG を読み、タスクごとに worktree と branch を切り、`claude -p` worker を起動し、完了マーカと verify script の成功を条件に main へ fast-forward merge する設計である。

この設計は、近年の software engineering agent 研究で重視されている次の方向性と合っている。

- 自然言語プロンプトだけでなく、実行可能な検証環境を報酬または merge gate として使う
- タスクを isolation した実行環境で解かせ、trajectory と検証結果を残す
- 1 つの出力を信じず、複数候補、複数 verifier、再試行、選別を組み合わせる
- agent が読む前提でコード、ドキュメント、ログ、メトリクスを整備する

一方で、現状の RALPH には安全性と自己改善性の両方で重要な欠落がある。特に、F#/.NET リポジトリにもかかわらず MoonBit 前提の worker prompt と generic verify が残っている点は、最初に直す必要がある。

## 調査した知見

### SWE-bench 系

SWE-bench は、実 GitHub issue と PR から作った repo-level software engineering benchmark である。重要なのは、単発のコード生成ではなく、既存コードベースを読み、複数ファイルを変更し、実行環境で検証する課題として設計されている点である。

RALPH への示唆:

- タスクは自然言語だけでなく、実行可能な repo 状態、検証スクリプト、期待される failure mode とセットで定義する
- 小さな関数生成ではなく、複数ファイル、テスト、コントラクト、アーキテクチャ制約を含む評価タスクを持つ
- benchmark は固定しすぎると汚染されるため、ローカル履歴や fuzz failure から新鮮なタスクを継続生成する

参照:

- https://arxiv.org/abs/2310.06770

### SWE-agent と Agent-Computer Interface

SWE-agent は agent-computer interface の設計が性能を左右することを示した。単に shell を与えるのではなく、検索、閲覧、編集、実行、フィードバックを agent に読みやすい形に整えることが重要である。

RALPH への示唆:

- worker に自由な shell だけを渡すのではなく、失敗ログ、SARIF、coverage、trace、diff を読みやすいコマンドとして提供する
- verify failure の出力は人間用の長いログだけでなく、agent が次の修正に使える structured summary にする
- status marker は自由文から grep するだけでなく、できれば `task-result.json` のような機械可読 artifact に寄せる

参照:

- https://arxiv.org/abs/2405.15793

### Agentless

Agentless は、複雑な自律 agent でなくても localization、repair、patch validation の単純な三段構成で強い baseline になることを示している。

RALPH への示唆:

- いきなり全自律化しない
- タスクの種類によっては「探索 worker」と「修正 worker」と「検証 worker」を分ける
- ループを複雑化する前に、localization と validation を機械化する

参照:

- https://arxiv.org/abs/2407.01489

### SWE-Gym

SWE-Gym は real-world repo、実行環境、unit tests、自然言語タスクを含む training environment として設計されている。さらに agent trajectory から verifier を訓練し、inference-time scaling に使う方向を示している。

RALPH への示唆:

- worker の全 trajectory、diff、test result、blocked reason を後から評価できる形式で保存する
- verify success だけでなく、trajectory quality、失敗カテゴリ、修正回数を保存する
- 将来的には repository-local verifier を訓練しなくても、ルールベース verifier と LLM reviewer を組み合わせて近い効果を狙える

参照:

- https://arxiv.org/abs/2412.21139

### SWE-smith と SWE-rebench

SWE-smith は既存コードベースから大量の task instance を合成する方向を示している。SWE-rebench は新鮮な interactive SWE task を継続的に抽出し、汚染されにくい評価を作る方向を示している。

RALPH への示唆:

- このリポジトリ専用の `.ralph/evals/` を持つ
- 過去の bug fix、Schemathesis failure、OpenAPI 変更、DSL 変更、mutation から自前タスクを生成する
- 評価タスクは固定 seed のみではなく、日次または週次で新鮮なケースを足す

参照:

- https://arxiv.org/abs/2504.21798
- https://arxiv.org/abs/2505.20411

### CodeMonkeys

CodeMonkeys は複数 trajectory を生成し、model-generated tests と selection trajectory によって候補を選ぶ test-time scaling を行っている。単一 agent の一発勝負ではなく、serial compute と parallel compute を使い分ける。

RALPH への示唆:

- 1 タスク 1 worker だけでなく、`attempt_count` を指定して複数 worker を並列実行する
- 各 attempt は別 branch、別 worktree、別 log を持つ
- verify を通った候補が複数ある場合は selector が選ぶ
- selector は「verify green」だけでなく、差分の小ささ、テスト追加、coverage、SARIF、アーキテクチャ違反、レビュー結果を見て選ぶ

参照:

- https://arxiv.org/abs/2501.14723

### SWE-Replay

SWE-Replay は過去 trajectory を再利用し、毎回ゼロから探索するコストを下げる方向を示している。

RALPH への示唆:

- blocked worktree を捨てず、探索メモリとして残す
- 次 attempt では前 attempt の「有効だった探索」「間違った仮説」「失敗ログ」を要約して渡す
- 同じタスクの再実行では、初回探索を再利用して修正フェーズから始める

参照:

- https://arxiv.org/abs/2601.22129

### Satori-SWE、SWE-Dev、Trae Agent

これらは training scaling、inference scaling、ensemble reasoning、generation/pruning/selection の分離を示している。大規模モデル訓練はこのリポジトリの直近スコープ外だが、候補生成と選別、テスト合成、trajectory 蓄積は導入できる。

RALPH への示唆:

- 大規模 RL は後回し
- まずは task-local に候補生成、検証、選別、失敗分類を実装する
- selector は早期には rule-based でよい
- 後から LLM reviewer を増やせる設計にする

参照:

- https://arxiv.org/abs/2505.23604
- https://arxiv.org/abs/2506.07636
- https://arxiv.org/abs/2507.23370

### Multi-Agent Verification と test-time scaling

Multi-Agent Verification は verifier の数と観点を増やすことで test-time compute をスケールする方向を示している。Scaling Test-time Compute for LLM Agents は、parallel sampling、sequential revision、verifier、merge/selection、rollout diversity を体系的に扱っている。

RALPH への示唆:

- verifier を単一 script から複数観点へ拡張する
- reviewer は binary verdict と根拠を出す
- list-wise selection を導入し、候補を個別採点するだけでなく相対比較する
- reflection は常時ではなく、失敗時や選別時に限定する

参照:

- https://arxiv.org/abs/2502.20379
- https://arxiv.org/abs/2506.12928

### PAGENT

PAGENT は failed patch の原因分類と program analysis を使った補修に焦点を当てている。型推論や CFG exploration など、LLM が苦手な領域を静的解析で補う考え方が重要である。

RALPH への示唆:

- blocked reason を自由文で終わらせず、taxonomy に分類する
- F# ではコンパイルエラー、型推論、宣言順、project file の compile order、Option/Result の取り扱いを失敗分類に含める
- よくある失敗は prompt ではなく analyzer、lint、verify に昇格する

参照:

- https://arxiv.org/abs/2506.17772

### OpenAI Harness Engineering

OpenAI の Harness Engineering 記事は、agent-first 開発では repository knowledge を system of record にし、AGENTS.md は巨大な説明書ではなく目次にするべきだと述べている。また、ログ、メトリクス、trace、UI 状態を agent が直接読めるようにする「agent legibility」を重視している。

RALPH への示唆:

- `AGENTS.md` に失敗を無限追記し続けない
- `docs/agent-harness/` のような構造化知識ベースへ移す
- 機械検査可能なルールは linter、test、verify、SARIF にする
- trace、SARIF、coverage、verify log を task 単位で要約する
- agent が読めない Slack、口頭判断、外部メモは存在しないものとして扱う

参照:

- https://openai.com/index/harness-engineering/

### Codex agent loop と Codex prompting

Codex agent loop の解説は、sandbox、developer instructions、AGENTS.md、environment context、tools の関係を明確にしている。Codex prompting guide は coding agent では過剰なプロンプトより、短く明確な developer prompt と少ない tool surface が有効だと述べている。

RALPH への示唆:

- worker prompt は短く、矛盾をなくす
- 言語固有コマンドは prompt 内に複数箇所で重複させず、単一の正本に寄せる
- tools は必要最小限にする
- タスク固有指示とリポジトリ規約を混ぜすぎない

参照:

- https://openai.com/index/unrolling-the-codex-agent-loop/
- https://cookbook.openai.com/examples/gpt-5-codex_prompting_guide

### Anthropic Building Effective Agents

Anthropic は、まず単純な workflow から始め、必要になった時だけ agentic complexity を増やすべきだとしている。framework は抽象化で prompt と response を見えにくくすることがあるため、debuggability を重視する。

RALPH への示唆:

- RALPH は complex framework ではなく、shell script と JSON/TOML artifact の単純な workflow として保つ
- まず fail-closed verify と structured logs を入れる
- multi-agent 化は task attempt と verifier の明確な interface ができてから行う

参照:

- https://www.anthropic.com/engineering/building-effective-agents

### Hermes Agent と Hermes Agent Self-Evolution

Hermes Agent は Nous Research の self-improving agent framework である。README 上では、agent-curated memory、autonomous skill creation、skills self-improve during use、past conversation search、isolated subagents、RPC 経由の tool 実行、複数 terminal backend、batch trajectory generation、Atropos RL environments、trajectory compression を主要機能としている。重要なのは「agent の能力」を単発の prompt ではなく、記憶、skill、tool、実行 trace、評価環境の閉ループとして扱っている点である。

Hermes Agent Self-Evolution は、DSPy + GEPA によって skill、tool description、system prompt、将来的には tool implementation code を進化対象にする。公開 README では、現在の skill/prompt/tool を読み、eval dataset を生成し、execution trace を GEPA optimizer に渡し、candidate variants を評価し、tests、size limits、benchmarks などの constraint gates を通った best variant を PR 化する流れが示されている。GPU training ではなく API call による text-space optimization である点も、このリポジトリに取り込みやすい。

RALPH への示唆:

- `ralph-task` skill、worker prompt、tool description、verifier prompt を「人間が手で直す静的文書」ではなく、評価付きで進化できる artifact として扱う
- self-evolution は main へ直接 commit しない。candidate branch を作り、eval suite、size limit、semantic preservation、human review を通して PR または local branch にする
- execution trace は「失敗ログ」だけでなく、prompt/skill 改善の入力データとして保存する
- skill/prompt の肥大化を防ぐため、Hermes と同様に size gate と semantic preservation gate を必須にする
- Atropos/RL のような本格 training は直近では採用しないが、`.ralph/evals/` と trajectory dataset は将来の training data 互換を意識して保存する

参照:

- https://github.com/NousResearch/hermes-agent
- https://github.com/NousResearch/hermes-agent-self-evolution

### Pi Agent

Pi Agent は Mario Zechner の `pi-coding-agent` を中心とする TypeScript 製の最小指向 coding harness である。README では、標準では `read`、`write`、`edit`、`bash` の 4 tools を与え、sub-agents や plan mode のような重い機能は core に入れず、必要なら TypeScript extensions、skills、prompt templates、themes、Pi packages で拡張する方針を取っている。

Pi は context file として global、親ディレクトリ、現在ディレクトリの `AGENTS.md` または `CLAUDE.md` を起動時に読み、project/global の `SYSTEM.md` や `APPEND_SYSTEM.md` で system prompt を差し替えまたは追記できる。また session には `/tree`、`/fork`、`/clone`、`/compact` があり、過去の会話分岐や compaction を明示的に扱う。拡張では custom tools、sub-agents、custom compaction、permission gates、path protection、git checkpointing、SSH/sandbox execution、MCP integration などを追加できる。一方、Pi packages は full system access を持つため、第三者 package は source review と pinning が必要だと明記されている。

RALPH への示唆:

- RALPH orchestrator core は小さく保ち、selector、verifier、reporter、sandbox policy、compaction は extension-like な外部 script contract に寄せる
- worker に渡す tool surface は増やしすぎず、必要な capability は task-specific skill または verifier/report command として読み込ませる
- session tree、fork、clone、compact に相当する artifact を `.ralph/sessions/` と `.ralph/compactions/` に残し、retry と replay の入力にする
- prompt template、skill、extension は project-local と user/global を分け、project run では lock file または allowlist で読み込み元を固定する
- external package や extension を導入する場合は full-system-access 前提で扱い、pinning、source review、hash 記録、sandbox policy を required gate にする
- Pi の「公開 OSS session data がモデル、prompt、tool、eval 改善に役立つ」という考えを、まずは private repository-local session dataset として採用する

参照:

- https://github.com/earendil-works/pi
- https://github.com/earendil-works/pi/blob/main/packages/coding-agent/README.md
- https://agent-safehouse.dev/docs/agent-investigations/pi

## 現状診断

### 強み

現在の RALPH には次の良い土台がある。

- DAG による依存関係管理
- task ごとの worktree 隔離
- task ごとの branch
- `halt_before` による人間レビュー待ち
- `serial_only` による並列競合の抑止
- verify script の exit code を merge gate にする設計
- failed worker の worktree を残す設計
- `stream-json` log の保存
- `.claude/scripts/emit-otel.py` による tool call trace 送信
- `.claude/scripts/sarif-to-lessons.py` による SARIF からの失敗教訓抽出
- `apps/api-fsharp/ci.sh` による build、format、lint、coverage、architecture、Pact、ZAP、Schemathesis、SARIF merge

### 最重要の欠陥

#### 1. Worker prompt と skill の言語前提が分裂している

該当ファイル:

- `.claude/plugins/ralph-orchestrator/prompts/task-prompt.tmpl.md`
- `.claude/plugins/ralph-orchestrator/prompts/worker-contract.md`
- `.claude/plugins/ralph-orchestrator/agents/ralph-worker.md`
- `.claude/plugins/ralph-orchestrator/skills/ralph-task/SKILL.md`

問題:

- prompt template と worker contract は MoonBit の `moon check`、`moon test`、`moon info`、`moon fmt` を完了条件にしている
- `ralph-task` skill は F#/.NET 版の検証フローを記述している
- 矛盾した指示が同一 worker に渡る
- agent がどちらを正本として扱うか不安定になる

影響:

- verify が正しくても worker 自身が間違ったコマンドで時間を浪費する
- done marker を出す条件が曖昧になる
- 失敗時の blocked reason がノイズ化する

#### 2. Generic verify が fail-open になっている

該当ファイル:

- `.claude/plugins/ralph-orchestrator/scripts/verify/_generic.sh`

問題:

- `moon` CLI が無い場合に `exit 0` する
- F# リポジトリで verify script が未指定またはパス解決に失敗すると、generic verify に落ちて成功扱いになる可能性がある

影響:

- 実テストが走らず main に merge され得る
- RALPH の最重要 gate が信用できない

方針:

- このリポジトリでは project-local `.ralph/verify/_generic.sh` を必須にする
- plugin generic verify は unknown project では fail-closed にする
- verify path が指定されていて存在しない場合は fallback せず fail する

#### 3. Baseline test count が F# に対応していない

該当ファイル:

- `.claude/plugins/ralph-orchestrator/scripts/lib/worker.sh`

問題:

- baseline test count capture が MoonBit の `moon test` 出力を前提にしている
- `.ralph/capture-baseline.sh` が無い場合、F# テスト数は 0 になりやすい

影響:

- テスト削除やカテゴリ漏れが検出されない
- test count regression gate が機能しない

方針:

- `.ralph/capture-baseline.sh` を追加し、`dotnet test` の TRX または console summary から F# test count を取る
- count 取得不能は 0 ではなく failure にするか、explicit opt-out を必要にする

#### 4. Rebase 後 verify が無い

該当ファイル:

- `.claude/plugins/ralph-orchestrator/scripts/lib/merge.sh`

問題:

- worker 完了後に verify を実行し、その後 `git rebase main` してから fast-forward merge している
- rebase 後のコードに対して verify を再実行していない

影響:

- 並列 task の merge 順によって post-rebase で壊れる可能性がある
- fast-forward merge の直前 gate として不十分

方針:

- `merge_run` の rebase 成功後、main merge 前に同じ verify を再実行する
- 可能なら merged main 上で smoke verify も実行する

#### 5. Task ID、branch 名、worktree path の入力検証が弱い

該当ファイル:

- `.claude/plugins/ralph-orchestrator/scripts/lib/worker.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/dag.sh`

問題:

- task id が branch 名、worktree path、log file path に使われる
- 現状の lint は重複や依存参照を主に見ており、safe identifier の検査が薄い

影響:

- task id に空白、`..`、shell metacharacter、slash の意図しない組み合わせが入ると path traversal や command risk になる

方針:

- task id は `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` のような安全な形式に制限する
- branch prefix と worktree prefix も安全性を検査する
- log file path は canonicalize し、`.ralph/logs/` 配下から出ないことを確認する

#### 6. AGENTS.md が知識ベースと失敗ログを兼ね始めている

該当ファイル:

- `AGENTS.md`
- `.claude/scripts/sarif-to-lessons.py`

問題:

- SARIF からの教訓が AGENTS.md 末尾に追記される
- 短期的には有用だが、長期的には巨大化、重複、古い教訓、検証不能な自然文が増える

影響:

- context を圧迫する
- agent が古い制約を重要視する
- 機械検査へ昇格すべきルールが文章のまま残る

方針:

- AGENTS.md は目次にする
- 失敗ログは `docs/agent-harness/failures.jsonl` または `.ralph/lessons/` に構造化保存する
- AGENTS.md には「最新の頻出失敗を見る場所」だけを書く
- 一定頻度を超えた失敗は lint、test、verify に昇格する

#### 7. 候補生成と選別が無い

問題:

- 現状は基本的に 1 task 1 worker 1 commit
- 一つの candidate が green なら merge する

影響:

- LLM の stochasticity を活かせない
- green でも過剰 diff、不十分なテスト、設計劣化を含む候補が選ばれる
- 失敗した trajectory から学習できない

方針:

- attempt fanout を導入する
- 複数候補の patch bundle を作る
- selector が rule-based score と verifier verdict で winner を選ぶ

#### 8. Cost、duration、retry の制御が弱い

問題:

- README に API コスト上限なしと明記されている
- worker の最大時間、最大 tool call、最大 retry、最大 token/cost が state にない

影響:

- runaway task のコストが読めない
- 改善施策の費用対効果を測れない

方針:

- task ごとに `budget_minutes`、`budget_attempts`、`budget_usd_estimate`、`max_tool_calls` を持たせる
- worker logs から elapsed time、tool count、exit reason を集計する
- cost が取れない場合も duration と token usage surrogate を残す

#### 9. Skill、prompt、tool description が進化対象として管理されていない

該当ファイル:

- `.claude/plugins/ralph-orchestrator/prompts/`
- `.claude/plugins/ralph-orchestrator/skills/`
- `.claude/plugins/ralph-orchestrator/agents/`
- `.ralph/` 配下に今後追加する skill/prompt

問題:

- worker prompt や skill は人間が直接編集する静的文書として扱われている
- どの失敗 trace がどの prompt/skill 改善に結びついたかを追跡できない
- prompt が肥大化しても size gate や semantic preservation gate が無い

影響:

- 失敗からの改善が再現不能になる
- prompt 改善が局所最適または過学習になっても検出しづらい
- skill や prompt の品質を RALPH eval suite で測れない

方針:

- skill、prompt、tool description を versioned evolvable artifact として扱う
- improvement candidate は直接 main に入れず、candidate branch と eval report を必須にする
- skill/prompt ごとに size limit、purpose statement、semantic preservation checklist、eval target を持たせる
- execution trace から prompt/skill 改善候補を生成する `ralph-orch evolve` を後段で追加する

#### 10. Extension と package の trust model が無い

問題:

- 今後 selector、verifier、reporter、sandbox、compaction を拡張していくと、RALPH core に機能が集中するか、外部 script/package を読み込む必要が出る
- 外部 package や project-local extension は、実質的に任意コード実行権限を持つ
- 読み込み元、バージョン、hash、許可権限を記録する仕組みがない

影響:

- 自己改善の名目で危険な extension を読み込む経路ができる
- reproducibility が落ちる
- verifier や selector の結果が、どの extension version によるものか追跡できない

方針:

- RALPH core は小さく保ち、extension-like contract を明示する
- `.ralph/policy.toml` と `.ralph/extensions.lock` で extension の読み込み元、version、hash、許可権限を固定する
- third-party extension は default deny とし、source review と pinning を required にする
- extension が出す artifact schema を固定し、core は JSON artifact を読むだけにする

## 設計原則

### 原則 1: Verify は fail-closed

検証スクリプトが無い、実行できない、テスト数が取れない、SARIF が壊れている、Docker が無い、DB が起動できない場合は成功扱いにしない。

例外として高速モードを許す場合は、環境変数ではなく task metadata に明示し、merge 対象外または human gate 必須にする。

### 原則 2: プロンプトより機械検査

AGENTS.md や prompt に「やってはいけない」と書くだけでは不十分である。

次の順で昇格させる。

1. 失敗ログに記録
2. taxonomy に分類
3. 反復するなら verifier、lint、test、ast-grep、architecture test に昇格
4. AGENTS.md はルールの所在だけを示す

### 原則 3: Repository knowledge が正本

agent が読めない情報は、実行時には存在しないものとして扱う。

設計判断、運用判断、失敗からの教訓、評価指標、タスク計画はリポジトリ内に置く。

### 原則 4: 小さな task と明確な boundary

worker は 1 task に集中する。編集対象は明示し、例外を減らす。複数 layer にまたがる変更は DAG に分割する。

### 原則 5: Candidate は選別してから merge

verify green は最低条件であり、品質の十分条件ではない。

merge する候補は、テスト品質、diff 範囲、アーキテクチャ適合、SARIF、レビュー観点を通した selector で選ぶ。

### 原則 6: 失敗 worktree は資産

blocked worktree は単なる残骸ではなく、次 attempt の探索履歴である。

失敗時には次を残す。

- hypothesis
- 読んだ主要ファイル
- 実行したコマンド
- failure category
- 最小再現
- 修正候補
- human decision が必要な点

### 原則 7: 自律度は段階的に上げる

最初から auto-push、auto-merge、multi-agent selection を全開にしない。

自律度を次の段階で上げる。

1. dry-run と local branch commit のみ
2. verify green で local merge
3. selector green で local merge
4. protected branch へ PR 作成
5. 条件付き auto-merge
6. 条件付き auto-push

### 原則 8: Skill、prompt、tool は評価付きで進化させる

Hermes Agent Self-Evolution の示唆を採用し、skill、prompt、tool description は静的な運用文書ではなく、eval suite と constraint gate を通して改善する対象にする。

ただし、自己改善は常に candidate branch で行い、次を満たすまで main へ入れない。

- 対象 artifact の purpose が変わっていない
- size limit を超えていない
- seed eval と fresh eval の双方で悪化していない
- failure taxonomy 上の既知失敗を減らしている
- 人間または required verifier が semantic drift なしと判断している

### 原則 9: Core は小さく、拡張は lock する

Pi Agent の最小 core 方針を採用し、RALPH core は DAG、state、worker spawn、verify、merge、report の薄い実行器に留める。selector、verifier、reporter、compaction、sandbox policy は extension-like な script contract として追加する。

一方で、extension は任意コード実行と同等に扱う。読み込み元、version、hash、権限、artifact schema を `.ralph/extensions.lock` と `.ralph/policy.toml` に固定し、default deny にする。

### 原則 10: Session tree と compaction を artifact として残す

retry と replay を強くするため、単なる stream log ではなく session tree、fork point、compaction summary を保存する。Pi の `/tree`、`/fork`、`/clone`、`/compact` に相当する構造を RALPH artifact として持つ。

これにより、次 attempt は「全部読み直す」のではなく、前 attempt の探索分岐と圧縮済み判断材料から開始できる。

## 改善ロードマップ

### P0: 安全な green gate を確立する

目的:

実テストが走っていないのに green になる経路を塞ぐ。

優先度:

最優先。これが終わるまで auto-merge/auto-push は使わない。

対象ファイル:

- `.claude/plugins/ralph-orchestrator/prompts/task-prompt.tmpl.md`
- `.claude/plugins/ralph-orchestrator/prompts/worker-contract.md`
- `.claude/plugins/ralph-orchestrator/agents/ralph-worker.md`
- `.claude/plugins/ralph-orchestrator/scripts/verify/_generic.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/verify.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/merge.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/dag.sh`
- `.ralph/verify/_generic.sh`
- `.ralph/capture-baseline.sh`

実装タスク:

1. F#/.NET 用 project-local generic verify を追加する
2. project-local capture-baseline を追加する
3. plugin generic verify を fail-open から fail-closed に変える
4. verify script の指定パスが存在しない場合は fallback せず fail する
5. worker prompt と worker contract から MoonBit 前提を除去する
6. `ralph-task` skill を唯一の言語固有契約にするか、prompt template と完全同期する
7. rebase 後 verify を追加する
8. task id の安全性 lint を追加する
9. auto_push の default を false にするか、tasks.toml で explicit に指定しない限り push しない
10. `git status --porcelain` が main worktree で dirty の場合は start を止める

F# generic verify の初期案:

```bash
#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

echo "::: ralph generic verify for ${TASK_ID:-?}"

dotnet build apps/api-fsharp --warnaserror
dotnet test apps/api-fsharp/tests/SalesManagement.Tests --no-build
dotnet fantomas --check apps/api-fsharp/src apps/api-fsharp/tests

if [ "${RALPH_FULL_CI:-0}" = "1" ]; then
  (
    cd apps/api-fsharp
    ZAP_ENABLED="${ZAP_ENABLED:-0}" \
    SCHEMATHESIS_ENABLED="${SCHEMATHESIS_ENABLED:-0}" \
    bash ci.sh
  )
fi
```

capture-baseline の初期案:

```bash
#!/usr/bin/env bash
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

dotnet test apps/api-fsharp/tests/SalesManagement.Tests \
  --logger "trx;LogFileName=baseline.trx" \
  --results-directory "$tmp" >/dev/null

python3 - "$tmp/baseline.trx" <<'PY'
import pathlib
import sys
import xml.etree.ElementTree as ET

trx = pathlib.Path(sys.argv[1])
if not trx.exists():
    raise SystemExit("baseline.trx not found")

root = ET.parse(trx).getroot()
ns = {"t": root.tag.split("}")[0].strip("{")} if root.tag.startswith("{") else {}
total = 0
for counters in root.findall(".//t:Counters", ns) if ns else root.findall(".//Counters"):
    total = int(counters.attrib.get("total", "0"))
    break
if total <= 0:
    raise SystemExit("failed to capture positive test count")
print(total)
PY
```

受け入れ条件:

- `.ralph/verify/_generic.sh` が存在しない F# task は失敗する
- `moon` が無いことを理由に verify が成功しない
- `dotnet build`、`dotnet test`、`fantomas --check` が最低限の generic verify として走る
- rebase 後に verify が再実行される
- unsafe task id を含む `tasks.toml` は `ralph-orch lint` で失敗する
- P0 完了まで `auto_push` は false 推奨で文書化される

### P1: Agent 向け知識ベースを分離する

目的:

AGENTS.md の肥大化を防ぎ、agent が必要な情報へ段階的に辿れる構造を作る。

対象ファイル:

- `AGENTS.md`
- `docs/agent-harness/index.md`
- `docs/agent-harness/ralph-architecture.md`
- `docs/agent-harness/quality-score.md`
- `docs/agent-harness/failure-taxonomy.md`
- `docs/agent-harness/failures.jsonl`
- `.claude/scripts/sarif-to-lessons.py`
- `.claude/scripts/agent-knowledge-lint.py`

ディレクトリ案:

```text
docs/agent-harness/
├── index.md
├── ralph-architecture.md
├── ralph-operational-runbook.md
├── quality-score.md
├── failure-taxonomy.md
├── failures.jsonl
├── verifier-contract.md
├── eval-suite.md
└── decisions/
    ├── 2026-05-13-ralph-fsharp-verify.md
    └── ...
```

AGENTS.md の役割:

- 開発スタイル、命名規約、重要な入口だけを残す
- RALPH の詳細は `docs/agent-harness/index.md` に誘導する
- 自動追記領域は短いサマリまたはリンクにする

failures.jsonl の schema:

```json
{
  "timestamp": "2026-05-13T00:00:00Z",
  "task_id": "S1-A",
  "attempt": 1,
  "phase": "verify",
  "category": "compile_error",
  "tool": "dotnet",
  "rule_id": "FS0039",
  "message": "The value or constructor is not defined",
  "file": "apps/api-fsharp/src/SalesManagement/Domain/Workflows.fs",
  "line": 123,
  "commit": "abc123",
  "worktree": "../mr-ralph-S1-A",
  "resolution": "pending",
  "promote_to": "test"
}
```

failure taxonomy 初期案:

- `prompt_contract_mismatch`
- `missing_verify`
- `verify_fail_open`
- `compile_error`
- `type_error`
- `fsharp_compile_order`
- `format_error`
- `test_failure`
- `flaky_test`
- `coverage_regression`
- `architecture_violation`
- `openapi_contract_violation`
- `pact_failure`
- `schemathesis_failure`
- `zap_failure`
- `security_finding`
- `dependency_vulnerability`
- `overbroad_diff`
- `missing_test`
- `insufficient_task_spec`
- `human_decision_required`
- `environment_missing`
- `timeout`
- `cost_budget_exceeded`

受け入れ条件:

- AGENTS.md が RALPH の巨大な詳細を抱え込まない
- SARIF lesson は JSONL に保存される
- `agent-knowledge-lint.py` が次を検査する
  - `docs/agent-harness/index.md` から主要文書へリンクがある
  - failure taxonomy に存在しない category が `failures.jsonl` に無い
  - AGENTS.md の自動追記領域が一定行数を超えない
  - stale な decision が明示されている

### P2: RALPH 自体の評価スイートを作る

目的:

ハーネス改善が本当に agent の成功率、コスト、品質を改善しているかを測る。

対象ファイル:

- `.ralph/evals/`
- `.ralph/evals/tasks/*.toml`
- `.ralph/evals/fixtures/`
- `.ralph/evals/run-eval.sh`
- `.ralph/evals/report.py`
- `docs/agent-harness/eval-suite.md`

評価タスク種別:

1. Compile-only repair
2. Unit test repair
3. Integration test repair
4. OpenAPI contract repair
5. DSL to Domain type update
6. Architecture rule violation repair
7. Schemathesis regression repair
8. Pact regression repair
9. Security finding repair
10. Documentation-only update
11. Refactor with no behavior change
12. Flaky test diagnosis

各 eval task が持つべき情報:

- task id
- natural language prompt
- initial patch または fixture branch
- allowed files
- hidden or public verify
- expected changed files
- expected failure category
- max budget
- pass/fail oracle

report 指標:

- `resolve_rate`
- `first_attempt_resolve_rate`
- `mean_attempts_to_green`
- `median_duration_seconds`
- `p95_duration_seconds`
- `verify_false_green_count`
- `post_merge_failure_count`
- `blocked_rate`
- `human_decision_required_rate`
- `overbroad_diff_rate`
- `missing_test_rate`
- `sarif_error_count`
- `coverage_delta`

受け入れ条件:

- 最低 10 件の seed eval task がある
- `run-eval.sh` が main を汚さず実行できる
- JSON と Markdown の report が出る
- RALPH 改善 PR は eval report の before/after を貼れる

### P3: Attempt fanout と selector を導入する

目的:

1 タスクに複数候補を生成し、検証と選別で最良候補を merge する。

対象ファイル:

- `.claude/plugins/ralph-orchestrator/scripts/lib/worker.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/state.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/dag.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/select.sh`
- `.claude/plugins/ralph-orchestrator/scripts/orchestrator.sh`
- `.ralph/selectors/default.sh`
- `docs/agent-harness/verifier-contract.md`

tasks.toml 拡張案:

```toml
[[tasks]]
id = "S1-A"
title = "F# generic verify を fail-closed にする"
phase = "S1"
files = [
  ".claude/plugins/ralph-orchestrator/scripts/verify/_generic.sh",
  ".ralph/verify/_generic.sh",
  ".ralph/capture-baseline.sh",
]
verify = ".ralph/verify/S1-A.sh"
attempt_count = 3
selector = ".ralph/selectors/default.sh"

[tasks.budget]
minutes = 45
max_tool_calls = 120
```

state.json 拡張案:

```json
{
  "tasks": {
    "S1-A": {
      "status": "selecting",
      "attempts": {
        "1": {
          "status": "verified",
          "branch": "ralph/S1-A/a1",
          "worktree": "../mr-ralph-S1-A-a1",
          "commit": "abc123",
          "verify": "passed",
          "score": 82
        },
        "2": {
          "status": "blocked",
          "block_reason": "compile_error"
        }
      },
      "selected_attempt": "1"
    }
  }
}
```

selector 入力:

- candidate commit sha
- diff stat
- changed files
- verify log
- SARIF summary
- coverage delta
- test count delta
- reviewer verdicts
- task metadata

selector score 初期案:

```text
score = 100
score -= 30 * sarif_error_count
score -= 15 * architecture_violation_count
score -= 10 * missing_test_flag
score -= 10 * overbroad_diff_flag
score -= 5  * changed_files_outside_expected_count
score += 10 * adds_regression_test_flag
score += 5  * coverage_improved_flag
```

受け入れ条件:

- `attempt_count = 1` の既存挙動は維持される
- `attempt_count > 1` で別 worktree、別 branch、別 log が作られる
- verify green candidate が複数ある場合に selector が 1 つを選ぶ
- selector の判断結果が artifact として残る
- 全 candidate failed の場合は task が blocked になる

### P4: Multi-verifier を導入する

目的:

verify script だけでは見落とす品質問題を、観点別 verifier で補う。

対象ファイル:

- `.ralph/verifiers/`
- `.ralph/verifiers/build.sh`
- `.ralph/verifiers/test.sh`
- `.ralph/verifiers/architecture.sh`
- `.ralph/verifiers/contract.sh`
- `.ralph/verifiers/security.sh`
- `.ralph/verifiers/test-quality.sh`
- `.ralph/verifiers/diff-scope.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/verify.sh`
- `docs/agent-harness/verifier-contract.md`

verifier result schema:

```json
{
  "name": "architecture",
  "status": "pass",
  "severity": "required",
  "score": 100,
  "summary": "No architecture category tests failed",
  "evidence": [
    {
      "type": "command",
      "command": "dotnet test ... --filter Category=Architecture",
      "exit_code": 0
    }
  ],
  "sarif": "ci-results/sarif/architecture.sarif"
}
```

verifier 種別:

- Required
  - build
  - unit/integration tests
  - format
  - task-specific verify
  - diff scope
- Strongly recommended
  - architecture
  - OpenAPI contract
  - SARIF error summary
  - test-quality
- Optional
  - security deep scan
  - ZAP
  - Schemathesis extended
  - mutation test

受け入れ条件:

- verifier ごとの JSON result が出る
- required verifier が 1 つでも fail なら merge されない
- optional verifier の failure は warning として selector score に影響する
- verifier summary が `ralph-orch status` または `ralph-orch report` で読める

### P5: 失敗分類と replay memory を導入する

目的:

失敗を次 attempt と今後の harness 改善に利用する。

対象ファイル:

- `.ralph/memory/`
- `.ralph/memory/failures.jsonl`
- `.ralph/memory/task-summaries/`
- `.ralph/sessions/`
- `.ralph/compactions/`
- `.claude/plugins/ralph-orchestrator/scripts/lib/failure.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/replay.sh`
- `.claude/plugins/ralph-orchestrator/prompts/retry-context.tmpl.md`

blocked artifact:

```json
{
  "task_id": "S1-A",
  "attempt": 2,
  "status": "blocked",
  "category": "compile_error",
  "root_cause": "F# compile order was changed without updating fsproj",
  "commands_run": [
    "dotnet build apps/api-fsharp --warnaserror"
  ],
  "files_read": [
    "apps/api-fsharp/src/SalesManagement/SalesManagement.fsproj"
  ],
  "files_changed": [
    "apps/api-fsharp/src/SalesManagement/Domain/Workflows.fs"
  ],
  "next_attempt_hint": "Check Compile Include order before editing Domain files",
  "human_decision_required": false
}
```

retry context:

- 前 attempt の root cause
- 失敗した command の最短ログ
- 読んだが不要だったファイル
- 有効だった仮説
- 禁止すべき方向

session tree artifact:

```json
{
  "session_id": "S1-A-a2",
  "task_id": "S1-A",
  "attempt": 2,
  "parent_session_id": "S1-A-a1",
  "fork_reason": "retry_after_compile_error",
  "compactions": [
    {
      "id": "c1",
      "source": ".ralph/logs/S1-A-a1.stream.jsonl",
      "summary_path": ".ralph/compactions/S1-A-a1-c1.md",
      "token_estimate_before": 92000,
      "token_estimate_after": 4300
    }
  ],
  "decision_points": [
    {
      "step": "localization",
      "chosen_path": "apps/api-fsharp/src/SalesManagement/Domain/Workflows.fs",
      "alternatives_rejected": [
        "apps/api-fsharp/src/SalesManagement/Api/Lots.fs"
      ],
      "reason": "compile error points to workflow result type mismatch"
    }
  ]
}
```

受け入れ条件:

- blocked task は taxonomy category を必ず持つ
- retry attempt は前 attempt summary を読む
- failures.jsonl から週次の top failure report が生成できる
- 反復頻度が threshold を超えた category は verify/lint 昇格候補になる
- retry attempt が session tree と compaction summary を artifact として残す
- compaction は原文 log への参照を保持し、要約だけを正本にしない

### P6: Observability と agent-legible report を整備する

目的:

worker、verify、merge、selector の状態を人間と agent の両方が読めるようにする。

対象ファイル:

- `.claude/scripts/emit-otel.py`
- `.claude/plugins/ralph-orchestrator/scripts/lib/report.sh`
- `.claude/plugins/ralph-orchestrator/scripts/orchestrator.sh`
- `docs/agent-harness/ralph-operational-runbook.md`

追加コマンド案:

```bash
/ralph-orch report <task-id>
/ralph-orch report --json <task-id>
/ralph-orch failures --since 7d
/ralph-orch quality
```

report に含める情報:

- task metadata
- attempts summary
- selected attempt
- elapsed time
- changed files
- commits
- verify results
- SARIF summary
- coverage delta
- failure category
- logs path
- trace id
- next action

OTel span 属性案:

- `ralph.task_id`
- `ralph.attempt`
- `ralph.phase`
- `ralph.worker_pid`
- `ralph.worktree`
- `ralph.branch`
- `ralph.status`
- `ralph.verify.exit_code`
- `ralph.selector.score`

受け入れ条件:

- task ごとの report が Markdown と JSON で出る
- Jaeger が無い場合も artifact は生成される
- Stop hook の失敗が main task を壊さない
- agent が report を読めば次 action を判断できる

### P7: 新鮮な評価タスク生成を導入する

目的:

固定 benchmark への過適合を避け、現在のリポジトリに即した評価を継続生成する。

生成元:

- git history の bug fix commit
- reverted commit
- Schemathesis failure
- ZAP finding
- Pact failure
- OpenAPI diff
- DSL diff
- mutation testing
- support harness 利用違反
- architecture test failure

生成 artifact:

```text
.ralph/evals/generated/
├── 2026-05-13-schemathesis-post-lots-invalid-schema/
│   ├── task.toml
│   ├── initial.patch
│   ├── verify.sh
│   └── README.md
```

生成タスクの基準:

- 初期状態で red になる
- 期待修正が単一関心に収まる
- hidden oracle または deterministic verify がある
- 既存 docs だけで解ける
- 外部ネットワークに依存しない

受け入れ条件:

- 週次で 3 件以上の fresh eval candidate を生成できる
- generated eval は人間レビュー後に seed eval へ昇格できる
- eval report で seed と fresh を分けて表示する

### P8: Security と cost guardrail を追加する

目的:

自律実行が増えても、秘密情報、外部通信、破壊的操作、コスト暴走を抑止する。

対象:

- `.claude/settings.local.json`
- `.claude/plugins/ralph-orchestrator/scripts/lib/dag.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/worker.sh`
- `.claude/plugins/ralph-orchestrator/scripts/lib/common.sh`
- `.ralph/policy.toml`
- `.ralph/extensions.lock`

policy.toml 案:

```toml
[security]
network = "deny"
allow_push = false
allow_external_curl = false
allow_docker = true
require_clean_main = true
safe_task_id_pattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$"

[budget]
default_minutes = 45
max_minutes = 180
default_attempts = 1
max_attempts = 5

[extensions]
default = "deny"
allow_project_local = true
allow_user_global = false
allow_third_party = false
```

extensions.lock 案:

```json
{
  "version": 1,
  "extensions": [
    {
      "name": "default-selector",
      "path": ".ralph/selectors/default.sh",
      "sha256": "...",
      "permissions": ["read-artifacts", "write-selection-result"],
      "reviewed_by": "human",
      "reviewed_at": "2026-05-13"
    }
  ]
}
```

guardrail:

- task id validation
- branch/worktree path validation
- command allowlist review
- destructive command denylist
- token/secret redaction in logs
- max duration
- max attempt count
- orphan process cleanup
- worktree cleanup report
- extension source/hash validation
- project-local/global context loading policy

受け入れ条件:

- unsafe task id は spawn 前に拒否される
- `auto_push = true` は policy で明示許可が必要
- timeout 超過 task は blocked になり、子 process が停止される
- log に secret pattern が出た場合は SARIF finding になる
- `.ralph/extensions.lock` に無い extension は実行されない
- user/global skill や extension の読み込みは project policy で明示許可が必要になる

### P9: 運用プロセスを定義する

目的:

ハーネス改善が一度きりで終わらず、継続的に自己改善する運用にする。

週次運用:

1. `ralph-orch quality` を確認する
2. top failure categories を見る
3. 反復失敗を verifier/lint/test に昇格する
4. fresh eval を seed eval に昇格する
5. AGENTS.md と docs の stale check を走らせる
6. auto-merge 設定を見直す

月次運用:

1. eval suite の resolve rate を確認する
2. cost と duration の傾向を見る
3. selector の false positive/false negative をレビューする
4. 不要になった prompt 指示を削る
5. verifier の重複を整理する

自律度の昇格条件:

```text
Level 0: dry-run only
Level 1: worker commit only
Level 2: local auto-merge, no push
Level 3: PR creation, human merge
Level 4: auto-merge for low-risk docs/tests/refactor
Level 5: auto-push for selected low-risk task class
```

Level 4 へ進む条件:

- seed eval resolve rate 70% 以上
- verify false green 0 件
- post-merge failure 0 件が 2 週間
- unsafe task id rejection test が green
- required verifiers が安定

Level 5 へ進む条件:

- Level 4 で 1 か月運用
- rollback 手順が検証済み
- branch protection と CI が remote 側で有効
- security scan が required gate

### P10: Skill/prompt/tool description の自己改善 lane を追加する

目的:

Hermes Agent Self-Evolution の考え方を、RALPH に安全な範囲で取り込む。対象は model weight ではなく、text-space artifact である skill、prompt、tool description、verifier instruction に限定する。

対象ファイル:

- `.ralph/evolution/`
- `.ralph/evolution/targets.toml`
- `.ralph/evolution/candidates/`
- `.ralph/evolution/reports/`
- `.claude/plugins/ralph-orchestrator/scripts/lib/evolve.sh`
- `.claude/plugins/ralph-orchestrator/scripts/orchestrator.sh`
- `docs/agent-harness/evolution-lane.md`

targets.toml 案:

```toml
[[targets]]
id = "ralph-task-skill"
kind = "skill"
path = ".claude/plugins/ralph-orchestrator/skills/ralph-task/SKILL.md"
purpose = "RALPH worker が F#/.NET タスクの検証契約を忘れないようにする"
max_bytes = 15000
eval_suite = ".ralph/evals/suites/worker-contract.toml"
semantic_preservation = [
  "F#/.NET の検証順を維持する",
  "verify 失敗時に done を出さない",
  "Co-Authored-By を付けない"
]

[[targets]]
id = "worker-contract-prompt"
kind = "prompt"
path = ".claude/plugins/ralph-orchestrator/prompts/worker-contract.md"
purpose = "spawn された worker の最小契約を定義する"
max_bytes = 12000
eval_suite = ".ralph/evals/suites/prompt-contract.toml"
```

candidate result schema:

```json
{
  "target_id": "ralph-task-skill",
  "candidate_id": "2026-05-13-001",
  "source_traces": [
    ".ralph/logs/P0-PROMPT.stream.jsonl",
    ".ralph/memory/failures.jsonl"
  ],
  "mutation_summary": "MoonBit 残骸を削除し、verify path missing を fail と明記した",
  "size_bytes_before": 8420,
  "size_bytes_after": 8012,
  "eval": {
    "passed": 12,
    "failed": 0
  },
  "semantic_preservation": "pass",
  "selected": true,
  "branch": "ralph/evolve/ralph-task-skill/2026-05-13-001"
}
```

処理フロー:

1. 対象 artifact と purpose を読む
2. failures.jsonl、session tree、compaction summary、eval failures を入力にする
3. candidate を複数生成する
4. size limit、forbidden phrase、semantic preservation checklist を検査する
5. seed eval と fresh eval を実行する
6. best candidate を branch に commit する
7. report を生成する
8. human review または required verifier を通った場合だけ merge 対象にする

受け入れ条件:

- skill/prompt/tool description の候補は main に直接書き込まれない
- candidate は必ず source trace と eval report を持つ
- size limit を超えた candidate は rejected になる
- seed eval が悪化した candidate は rejected になる
- semantic preservation が不明な場合は human review 必須になる
- evolution lane 自体の変更は high-risk として `halt_before=true` にする

採用しないこと:

- model weight training
- 本番 worker が実行中に system prompt を動的変更すること
- human review なしで skill/prompt の auto-push を行うこと
- third-party optimizer を lock なしで実行すること

## 推奨タスク DAG

初期導入では次の順で `.ralph/tasks.toml` を作る。

```toml
[meta]
schema_version = 1
default_model = "opus"
worker_pool_size = 2
worktree_prefix = "../mr-ralph-"
branch_prefix = "ralph/"
auto_merge = false
auto_push = false

[[tasks]]
id = "P0-VERIFY"
title = "F# generic verify を fail-closed にする"
phase = "P0"
size = "M"
files = [
  ".ralph/verify/_generic.sh",
  ".ralph/capture-baseline.sh",
  ".claude/plugins/ralph-orchestrator/scripts/verify/_generic.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/verify.sh",
]
depends_on = []
serial_only = true
verify = ".ralph/verify/P0-VERIFY.sh"
prompt_extra = """
MoonBit 前提の fail-open verify を排除し、F#/.NET の build/test/fantomas が走らない限り成功しないようにする。
"""

[[tasks]]
id = "P0-PROMPT"
title = "RALPH worker 契約を F#/.NET に統一する"
phase = "P0"
size = "M"
files = [
  ".claude/plugins/ralph-orchestrator/prompts/task-prompt.tmpl.md",
  ".claude/plugins/ralph-orchestrator/prompts/worker-contract.md",
  ".claude/plugins/ralph-orchestrator/agents/ralph-worker.md",
  ".claude/plugins/ralph-orchestrator/skills/ralph-task/SKILL.md",
]
depends_on = ["P0-VERIFY"]
verify = ".ralph/verify/P0-PROMPT.sh"

[[tasks]]
id = "P0-MERGE-GATE"
title = "rebase 後 verify と安全な task id lint を追加する"
phase = "P0"
size = "M"
files = [
  ".claude/plugins/ralph-orchestrator/scripts/lib/merge.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/dag.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/worker.sh",
]
depends_on = ["P0-VERIFY"]
serial_only = true
verify = ".ralph/verify/P0-MERGE-GATE.sh"

[[tasks]]
id = "P1-KNOWLEDGE"
title = "agent-harness 知識ベースと failure taxonomy を追加する"
phase = "P1"
size = "M"
files = [
  "AGENTS.md",
  "docs/agent-harness/index.md",
  "docs/agent-harness/ralph-architecture.md",
  "docs/agent-harness/failure-taxonomy.md",
  "docs/agent-harness/quality-score.md",
  ".claude/scripts/sarif-to-lessons.py",
]
depends_on = ["P0-PROMPT", "P0-MERGE-GATE"]
verify = ".ralph/verify/P1-KNOWLEDGE.sh"

[[tasks]]
id = "P2-EVAL"
title = "RALPH 評価スイートの seed tasks と report を追加する"
phase = "P2"
size = "L"
files = [
  ".ralph/evals/",
  "docs/agent-harness/eval-suite.md",
]
depends_on = ["P1-KNOWLEDGE"]
verify = ".ralph/verify/P2-EVAL.sh"

[[tasks]]
id = "P3-ATTEMPTS"
title = "attempt fanout と selector contract を導入する"
phase = "P3"
size = "L"
files = [
  ".claude/plugins/ralph-orchestrator/scripts/lib/worker.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/state.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/dag.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/select.sh",
  ".claude/plugins/ralph-orchestrator/scripts/orchestrator.sh",
  "docs/agent-harness/verifier-contract.md",
]
depends_on = ["P2-EVAL"]
serial_only = true
verify = ".ralph/verify/P3-ATTEMPTS.sh"

[[tasks]]
id = "P5-FAILURE-MEMORY"
title = "失敗分類、session tree、compaction artifact を追加する"
phase = "P5"
size = "M"
files = [
  ".ralph/memory/",
  ".ralph/sessions/",
  ".ralph/compactions/",
  ".claude/plugins/ralph-orchestrator/scripts/lib/failure.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/replay.sh",
  ".claude/plugins/ralph-orchestrator/prompts/retry-context.tmpl.md",
]
depends_on = ["P3-ATTEMPTS"]
verify = ".ralph/verify/P5-FAILURE-MEMORY.sh"

[[tasks]]
id = "P8-POLICY"
title = "extension/package trust model と policy lock を追加する"
phase = "P8"
size = "M"
files = [
  ".ralph/policy.toml",
  ".ralph/extensions.lock",
  ".claude/plugins/ralph-orchestrator/scripts/lib/dag.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/worker.sh",
  ".claude/plugins/ralph-orchestrator/scripts/lib/common.sh",
]
depends_on = ["P5-FAILURE-MEMORY"]
serial_only = true
verify = ".ralph/verify/P8-POLICY.sh"

[[tasks]]
id = "P10-EVOLUTION-LANE"
title = "skill/prompt/tool description の自己改善 lane を追加する"
phase = "P10"
size = "L"
files = [
  ".ralph/evolution/",
  ".claude/plugins/ralph-orchestrator/scripts/lib/evolve.sh",
  ".claude/plugins/ralph-orchestrator/scripts/orchestrator.sh",
  "docs/agent-harness/evolution-lane.md",
]
depends_on = ["P2-EVAL", "P5-FAILURE-MEMORY", "P8-POLICY"]
serial_only = true
halt_before = true
verify = ".ralph/verify/P10-EVOLUTION-LANE.sh"
prompt_extra = """
Hermes Agent Self-Evolution のように execution trace から skill/prompt/tool description 改善候補を作る。
ただし main へ直接 commit せず、candidate branch、size gate、semantic preservation、eval report、人間レビューを必須にする。
"""
```

## 品質指標

### Harness correctness

- `verify_false_green_count`: 0 を維持する
- `post_rebase_verify_failure_count`: 0 に近づける
- `post_merge_failure_count`: 0 を維持する
- `unsafe_task_rejected_count`: unsafe fixture で 100% reject
- `missing_verify_rejected_count`: 100% reject

### Agent productivity

- `resolve_rate`: eval task の解決率
- `first_attempt_resolve_rate`: 1 attempt での解決率
- `mean_attempts_to_green`
- `median_duration_seconds`
- `p95_duration_seconds`
- `blocked_rate`
- `human_decision_required_rate`

### Patch quality

- `overbroad_diff_rate`
- `missing_test_rate`
- `test_count_delta`
- `coverage_delta`
- `architecture_violation_count`
- `sarif_error_count`
- `selector_rejected_green_candidate_count`

### Knowledge quality

- `failure_taxonomy_unknown_count`: 0 を目標
- `stale_doc_count`
- `agents_md_line_count`
- `rules_promoted_to_lint_count`
- `repeated_failure_without_rule_count`
- `skill_prompt_candidate_count`
- `skill_prompt_candidate_rejected_by_size_count`
- `skill_prompt_candidate_rejected_by_eval_count`
- `semantic_drift_rejection_count`

### Cost and resource

- `worker_elapsed_seconds`
- `tool_call_count`
- `attempt_count`
- `timeout_count`
- `orphan_worktree_count`
- `orphan_process_count`
- `ci_minutes`
- `session_compaction_ratio`
- `replay_context_reuse_count`
- `extension_lock_miss_count`

## リスクと対策

### リスク 1: Verify が重くなりすぎて iteration が遅くなる

対策:

- `required fast verify` と `full verify` を分ける
- merge 前は fast verify 必須、夜間または R-frame で full verify
- task risk class によって ZAP/Schemathesis/Pact の実行範囲を変える

### リスク 2: Multi-agent 化でログと状態が複雑になる

対策:

- attempt fanout の前に report JSON schema を定義する
- state migration を明示する
- 既存 `attempt_count = 1` の動作を保つ

### リスク 3: LLM reviewer がもっともらしいが不正確な評価をする

対策:

- LLM reviewer は optional verifier として扱う
- required gate は実行可能な script、test、lint、SARIF に寄せる
- reviewer verdict は根拠ファイル、行、コマンドを必須にする

### リスク 4: AGENTS.md を削りすぎて worker が迷う

対策:

- AGENTS.md は目次として残す
- `docs/agent-harness/index.md` を短く保つ
- task prompt に必要なリンクを明示する
- doc lint でリンク切れを検出する

### リスク 5: Fresh eval が不安定で指標を壊す

対策:

- seed eval と fresh eval を分ける
- fresh eval は人間レビュー後に seed 昇格
- flaky 判定を taxonomy に持つ

### リスク 6: Auto-merge が危険な変更を取り込む

対策:

- P0 では auto_merge=false、auto_push=false
- diff-scope verifier を required にする
- high-risk file class を定義する
- DB migration、auth、security、CI、orchestrator 自身は `halt_before=true` を標準にする

### リスク 7: Skill/prompt 自己改善が prompt bloat や semantic drift を起こす

対策:

- max bytes を target ごとに設定する
- purpose statement と semantic preservation checklist を必須にする
- seed eval が悪化した candidate は reject する
- fresh eval だけに過適合しないよう seed/fresh を分ける
- candidate は branch に commit し、main へ直接書き込まない
- evolution lane は `halt_before=true` とする

### リスク 8: Extension や package が supply-chain risk になる

対策:

- default deny
- project-local extension だけを初期許可する
- user/global extension は policy で明示許可されるまで読まない
- third-party extension は source review、pinning、hash 記録を必須にする
- extension の実行権限を `read-artifacts`、`write-report`、`run-verify` のような capability に分ける
- extension が任意 shell を実行する場合は high-risk として human gate を要求する

high-risk file class:

- `.claude/plugins/ralph-orchestrator/scripts/lib/merge.sh`
- `.claude/settings*.json`
- `.github/`
- `apps/api-fsharp/migrations/`
- `apps/api-fsharp/src/SalesManagement/Api/Authentication*`
- `apps/api-fsharp/src/SalesManagement/Infrastructure/`
- `openapi.yaml`
- `AGENTS.md`

## 完了定義

この改善計画全体の完了は、次の状態を指す。

1. F#/.NET 用 verify が fail-closed で動作している
2. worker prompt、worker contract、skill の言語前提が一致している
3. verify path missing、unsafe task id、baseline capture failure が merge 前に拒否される
4. rebase 後 verify が実行される
5. agent-harness 知識ベースが AGENTS.md から分離されている
6. 失敗が taxonomy 付き JSONL として保存される
7. seed eval suite が存在し、before/after を測れる
8. attempt fanout と selector が `attempt_count > 1` で使える
9. verifier result が JSON/SARIF として保存される
10. `ralph-orch report <task-id>` で task の判断材料を読める
11. auto-merge/auto-push の自律度が policy で制御されている
12. 週次で failure から lint/test/verify への昇格ができる
13. session tree、fork point、compaction summary が retry/replay の入力として保存される
14. skill、prompt、tool description の自己改善 candidate が eval と constraint gate を通して PR/branch 化される
15. extension/package の読み込み元、hash、権限が lock され、未承認 extension が実行されない

## 最初の実行順

手戻りを避けるため、最初の実装順は次に固定する。

1. P0-VERIFY
2. P0-PROMPT
3. P0-MERGE-GATE
4. P1-KNOWLEDGE
5. P2-EVAL
6. P3-ATTEMPTS
7. P4-MULTI-VERIFIER
8. P5-FAILURE-MEMORY
9. P6-REPORT
10. P7-FRESH-EVAL
11. P8-POLICY
12. P9-OPERATIONS
13. P10-EVOLUTION-LANE

この順序の理由:

- verify が信用できない状態で自律度を上げると危険である
- prompt の矛盾は worker の無駄な試行を増やす
- eval suite が無いと改善効果を測れない
- attempt fanout は eval と selector が無いとコストだけ増える
- knowledge base と failure taxonomy が無いと自己改善が自然文ログに埋もれる
- skill/prompt/tool の自己改善は、eval suite、failure memory、policy lock が揃うまで入れない

## 直近の具体的な次アクション

### 1. P0 用ブランチを切る

```bash
git switch main
git switch -c ralph/p0-fail-closed-verify
```

### 2. `.ralph/` の最小構成を追加する

```text
.ralph/
├── tasks.toml
├── capture-baseline.sh
└── verify/
    ├── _generic.sh
    ├── P0-VERIFY.sh
    ├── P0-PROMPT.sh
    └── P0-MERGE-GATE.sh
```

### 3. P0 の negative tests を先に書く

テスト観点:

- `verify = ".ralph/verify/DOES_NOT_EXIST.sh"` は fail
- `.ralph/verify/_generic.sh` が無い場合は fail
- `id = "../bad"` は lint fail
- `id = "bad;rm"` は lint fail
- `moon` が無い環境でも F# verify が走る
- rebase 後に verify が呼ばれる

### 4. P0 実装後に dry-run だけ実行する

```bash
/ralph-orch lint
/ralph-orch dry-run
```

### 5. P0 が安定するまで auto_merge=false を維持する

```toml
[meta]
auto_merge = false
auto_push = false
```

## 参考文献と採用判断

| 参照 | 採用する点 | 直近で採用しない点 |
|---|---|---|
| SWE-bench | repo-level task と実行環境検証 | 外部 benchmark への最適化 |
| SWE-agent | agent-computer interface と読みやすい feedback | 大規模な専用 UI 実装 |
| Agentless | localization、repair、validation の単純化 | 完全 agentless 化 |
| SWE-Gym | trajectory と verifier の蓄積 | model fine-tuning |
| SWE-smith | task generation の考え方 | いきなり 1000 件規模生成 |
| SWE-rebench | fresh task と汚染対策 | 外部 dataset 依存 |
| CodeMonkeys | parallel trajectories と selection | 高額な大規模 sampling |
| SWE-Replay | 失敗 trajectory の再利用 | 複雑な branching replay |
| Satori-SWE | selection/mutation の考え方 | RL による self-evolve |
| SWE-Dev | test synthesis と trajectory scaling | 専用モデル訓練 |
| Trae Agent | generation/pruning/selection の分離 | leaderboard 指向の大規模 ensemble |
| Multi-Agent Verification | 観点別 verifier | LLM verifier を required gate にすること |
| PAGENT | failure taxonomy と静的解析補助 | CFG ベース補修の即時実装 |
| OpenAI Harness Engineering | repository knowledge、agent legibility、mechanical enforcement | 完全 agent-generated 開発への即時移行 |
| Codex agent loop | sandbox と instruction layering の整理 | 独自 Responses API harness 化 |
| Anthropic Effective Agents | 単純な workflow から始める | framework 追加 |
| Hermes Agent | memory、skill self-improvement、trajectory compression、subagent delegation | Hermes 本体への移行 |
| Hermes Agent Self-Evolution | execution trace から skill/prompt/tool description の candidate を生成し、constraint gate 後に PR 化 | model weight training、実行中 system prompt の動的変更 |
| Pi Agent | 最小 core、4 tool default、session tree/fork/compact、extension contract、context file layering | RALPH core の TypeScript 移植、未審査 package の導入 |
| Agent Safehouse Pi analysis | package/extension は full system access と見なし、sandbox・path protection・pinning を必須化 | 外部 extension を default allow にすること |

## 結論

RALPH の現在の方向性は正しいが、直近の改善順は自律度を上げる方向ではなく、まず fail-closed verify、契約統一、構造化ログ、評価スイートで土台を固めるべきである。

その後に attempt fanout、multi-verifier、selector、fresh eval generation を入れることで、LLM による自動コード生成を「運任せの反復」ではなく「測定可能で自己改善するハーネス」にできる。

Hermes Agent から追加で採用すべき点は、skill、prompt、tool description を execution trace と eval suite で改善する明示的な evolution lane である。Pi Agent から採用すべき点は、core を小さく保ち、session tree、fork、compaction、extension contract、package trust model を artifact と policy として扱うことである。

したがって、本計画は「P0 から P9 で安全な実行・評価・運用基盤を作る」ことに加え、「P10 で text-space artifact の自己改善を安全に始める」方針へ更新する。
