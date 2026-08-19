#!/usr/bin/env bash
# GitHub hosted runner の Azure APT ミラーが応答しない場合でも、
# CI が長時間停止しない取得先とタイムアウトを設定する。
set -euo pipefail

if [[ "$(id -u)" -ne 0 ]]; then
  echo "configure-ubuntu-apt.sh は root で実行してください" >&2
  exit 1
fi

readonly apt_mirror_file="/etc/apt/apt-mirrors.txt"
readonly apt_config_file="/etc/apt/apt.conf.d/99-ci-network"

# ubuntu-latest の deb822 source は mirror+file でこのファイルを参照する。
printf '%s\n' 'https://archive.ubuntu.com/ubuntu' >"$apt_mirror_file"

# runner image が従来形式の source を使う場合も Azure ミラーを残さない。
for source_file in \
  /etc/apt/sources.list \
  /etc/apt/sources.list.d/*.list \
  /etc/apt/sources.list.d/*.sources; do
  [[ -f "$source_file" ]] || continue
  sed -i -E \
    's#https?://azure\.archive\.ubuntu\.com/ubuntu/?#https://archive.ubuntu.com/ubuntu/#g' \
    "$source_file"
done

cat >"$apt_config_file" <<'EOF'
Acquire::Retries "3";
Acquire::http::Timeout "20";
Acquire::https::Timeout "20";
EOF

# Playwright の install --with-deps が後から実行する apt-get にも、
# 上記の永続設定がそのまま適用される。
apt-get update -q
