#!/usr/bin/env bash
set -euo pipefail

# 只把固定 source snapshot 交給真正的 Python exporter，不接受 English fallback。
readonly SOURCE_ROOT="${LAYA_MULTILINGUAL_SOURCE_ROOT:-models/laya-multilingual/source}"
readonly FIXTURE_PATH="${LAYA_MULTILINGUAL_FIXTURE_PATH:-test-data/multilingual-parity-fixtures.json}"
readonly STAGING_ROOT="${LAYA_MULTILINGUAL_STAGING_ROOT:-models/laya-multilingual/staging}"
readonly SITE_ROOT="${LAYA_REFERENCE_SITE:-/tmp/opencode/laya-reference-cpu-site}"

exec tools/laya-reference/run.sh tools/laya-reference/export.py \
  --source-root "$SOURCE_ROOT" \
  --fixture-path "$FIXTURE_PATH" \
  --output "$STAGING_ROOT" \
  "$@"
