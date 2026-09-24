#!/usr/bin/env bash
set -euo pipefail

readonly SITE_ROOT="${LAYA_REFERENCE_SITE:-/tmp/opencode/laya-reference-cpu-site}"
readonly SCRIPT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ ! -d "$SITE_ROOT" ]]; then
  printf 'BLOCKED: isolated Python target does not exist: %s\n' "$SITE_ROOT" >&2
  exit 2
fi

export PYTHONPATH="$SITE_ROOT:$SCRIPT_ROOT${PYTHONPATH:+:$PYTHONPATH}"
python3 -c 'from reference import validate_runtime_toolchain; validate_runtime_toolchain()' >/dev/null
exec python3 "$@"
