#!/usr/bin/env bash
# _generic.sh — default verify: リポジトリ統合 verify (scripts/verify.sh) へ委譲する
# Env: TASK_ID, BASELINE_TEST_COUNT, PLUGIN_ROOT (verify.sh にそのまま引き継ぐ)
#
# fail-closed 原則: verify.sh が無い場合もツールチェーン不足の場合も「スキップして
# 合格」にはしない。verify を通せない状態で auto_merge が main を汚染するのを防ぐ。
# 他構成のプロジェクトで使う場合は <repo>/scripts/verify.sh を用意するか、
# tasks.toml の verify でタスク固有スクリプトを指定して上書きする。
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

echo "::: generic verify for ${TASK_ID:-?} (baseline tests: ${BASELINE_TEST_COUNT:-0})"

if [[ ! -f scripts/verify.sh ]]; then
  echo "FAIL: scripts/verify.sh が見つかりません (fail-closed: 検証なしで合格にはしない)" >&2
  exit 1
fi

exec bash scripts/verify.sh
