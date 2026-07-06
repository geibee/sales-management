# shellcheck shell=bash
# scope.sh — verify.sh のスコープ判定ロジック。
#
# fail-closed の要 (「分類できないパス = 全検証」) をリグレッションから守るため、
# verify.sh 本体から分離して scripts/tests/classify-paths.bats で単体テストする。
# 呼び出し側は NEED_BACKEND / NEED_FRONTEND を 0 で初期化してから呼ぶこと。

classify_paths() {
  # 引数: 変更ファイルパス (改行区切りを while read で受ける)
  local path
  while IFS= read -r path; do
    [[ -z "$path" ]] && continue
    case "$path" in
      apps/api-fsharp/openapi.yaml | .spectral.yaml)
        # API 契約は両スコープに影響する (frontend 側で Spectral lint / 契約テストが走る)
        NEED_BACKEND=1; NEED_FRONTEND=1 ;;
      apps/api-fsharp/* | dsl/* | pacts/*)
        NEED_BACKEND=1 ;;
      apps/frontend/*)
        NEED_FRONTEND=1 ;;
      docs/* | *.md)
        ;; # ドキュメントのみの変更は検証対象外
      *)
        # 分類できないパス (ルート設定 / .claude / scripts など) は全部検証する
        NEED_BACKEND=1; NEED_FRONTEND=1 ;;
    esac
  done
}
