#!/usr/bin/env bats

setup() {
  # shellcheck source=../../apps/api-fsharp/scripts/artifact-filenames.sh
  source "$BATS_TEST_DIRNAME/../../apps/api-fsharp/scripts/artifact-filenames.sh"
}

@test "scc artifact のファイル名は NTFS 禁止文字を含まない" {
  timestamp=$(artifact_filename_timestamp "2026-07-14T19:17:57Z")
  filename="scc_${timestamp}.json"

  [ "$filename" = "scc_2026-07-14T19-17-57Z.json" ]
  [[ "$filename" =~ ^scc_[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}-[0-9]{2}-[0-9]{2}Z\.json$ ]]
  [[ ! "$filename" =~ [\":\<\>\|\*\?] ]]
  [[ "$filename" != *$'\r'* ]]
  [[ "$filename" != *$'\n'* ]]
}
