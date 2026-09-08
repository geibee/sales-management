#!/usr/bin/env bash
# F# 自動 / Java 手動の両経路が呼ぶ全量検証。途中失敗でも最後に監査する。
set -euo pipefail
cd "$(dirname "$0")/.."
export VERIFY_BASE_REF="${VERIFY_BASE_REF:-origin/main}"

target="${1:?fsharp または spring を指定してください}"
case "$target" in
  fsharp) scope=backend; application=apps/api-fsharp ;;
  spring) scope=spring; application=apps/api-spring ;;
  *) echo "未知の target: $target" >&2; exit 2 ;;
esac

QUALITY_RUN_DIR=$(python3 scripts/quality-run.py init --target "$target" --profile full)
export QUALITY_RUN_DIR
status=0
step() {
  local id="$1" tool="$2"
  shift 2
  python3 scripts/quality-run.py step --directory "$QUALITY_RUN_DIR" --id "$id" --tool "$tool" -- "$@"
}

finish() {
  local original=$?
  trap - EXIT
  if [[ "${QUALITY_DEFER_AUDIT:-0}" != 1 ]]; then
    python3 scripts/quality-evidence.py --target "$target" --directory "$QUALITY_RUN_DIR" --profile full || status=1
  else
    # CodeQL は workflow の build 後に生成される。最終監査は always ステップで実行する。
    echo "最終監査待ち: $QUALITY_RUN_DIR"
  fi
  (( original == 0 && status == 0 )) || exit 1
}
trap finish EXIT

step repo 'Repository gates' env VERIFY_SCOPE=repo bash scripts/verify.sh || status=1
step frontend 'Frontend gates' env VERIFY_SCOPE=frontend bash scripts/verify.sh || status=1
if [[ "$target" == fsharp ]]; then
  step setup 'Database setup' bash -c 'cd apps/api-fsharp && exec dotnet run --project tools/Migrator' || status=1
fi
if step light 'Light gates' env QUALITY_CHILD=1 VERIFY_SCOPE="$scope" bash scripts/verify.sh; then
  python3 scripts/quality-evidence.py --target "$target" --directory "$QUALITY_RUN_DIR" --profile light --collect-only || status=1
  step heavy 'Heavy gates' env QUALITY_HEAVY_CHILD=1 bash "$application/ci.sh" || status=1
  step faults 'Fault detection' python3 scripts/fault-check.py --target "$target" --output "$QUALITY_RUN_DIR/faults.json" || status=1
else
  status=1
fi
