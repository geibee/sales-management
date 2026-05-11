---
description: "DAG-based multi-session RALPH orchestrator (start/run/status/resume/dry-run/stop/logs/lint)"
argument-hint: "<subcommand> [args...]"
allowed-tools: ["Bash(${CLAUDE_PLUGIN_ROOT}/scripts/orchestrator.sh:*)"]
---

# Ralph Orchestrator

Run the orchestrator subcommand:

```!
"${CLAUDE_PLUGIN_ROOT}/scripts/orchestrator.sh" $ARGUMENTS
```

Subcommands:
- `start` — Start orchestrator in background. Reads `.ralph/tasks.toml`, spawns parallel workers, halts before R-frame tasks.
- `run <task-id>` — Run a single task in the foreground (debugging).
- `status` — Show running / completed / blocked tasks.
- `resume` — Resume after halt (clears halt marker).
- `dry-run` — Print the next batch of tasks that would run, no spawning.
- `stop` — SIGTERM all running workers; worktrees are preserved.
- `logs <task-id>` — Show child Claude's stream-json log for a task.
- `lint` — Validate tasks.toml structure and check for dependency cycles.

If no subcommand is provided, defaults to `status`.

The orchestrator runs subprocesses with `claude -p`, so the parent session
(this one) is not blocked. Use `/ralph-orch status` to poll.
