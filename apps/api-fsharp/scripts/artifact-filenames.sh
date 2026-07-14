#!/usr/bin/env bash

# ISO 8601 の UTC タイムスタンプを、NTFS を含む主要ファイルシステムで扱える
# artifact ファイル名用の値へ変換する。記録データ内の時刻表現は変更しない。
artifact_filename_timestamp() {
    local timestamp="$1"
    printf '%s\n' "${timestamp//:/-}"
}
