#!/usr/bin/env bash
set -euo pipefail

# 只有 Python 與 .NET parity 都指向同一 candidate 時才建立 verified version。
readonly STAGING_ROOT="${LAYA_MULTILINGUAL_STAGING_ROOT:-models/laya-multilingual/staging}"
readonly PUBLISH_ROOT="${LAYA_MULTILINGUAL_PUBLISH_ROOT:-models/laya-multilingual/versions}"
readonly CURRENT_POINTER="${LAYA_MULTILINGUAL_CURRENT_POINTER:-models/laya-multilingual/current-bundle.json}"
readonly PYTHON_REPORT="${LAYA_MULTILINGUAL_PYTHON_REPORT:-reports/multilingual-export-validation.json}"
readonly DOTNET_REPORT="${LAYA_MULTILINGUAL_DOTNET_REPORT:-reports/multilingual-dotnet-parity.json}"
readonly FIXTURE_PATH="${LAYA_MULTILINGUAL_FIXTURE_PATH:-test-data/multilingual-parity-fixtures.json}"

if [[ ! -f "$STAGING_ROOT/laya-bundle-manifest.json" ]]; then
  printf 'BLOCKED: staging bundle manifest is missing: %s\n' "$STAGING_ROOT/laya-bundle-manifest.json" >&2
  exit 2
fi
if [[ ! -f "$PYTHON_REPORT" || ! -f "$DOTNET_REPORT" ]]; then
  printf 'BLOCKED: Python and .NET parity reports are both required.\n' >&2
  exit 2
fi

readonly CANDIDATE_MANIFEST_SHA="$(sha256sum "$STAGING_ROOT/laya-bundle-manifest.json" | cut -d' ' -f1)"
readonly FIXTURE_SHA="$(sha256sum "$FIXTURE_PATH" | cut -d' ' -f1)"
if ! jq -e --arg sha "$CANDIDATE_MANIFEST_SHA" --arg fixtureSha "$FIXTURE_SHA" '
    .status == "complete" and .bundle.manifestSha256 == $sha and
    .fixtureSha256 == $fixtureSha and
    (.failedFixtures | length) == 0
  ' "$PYTHON_REPORT" >/dev/null; then
  printf 'FAILED: Python ORT report does not validate the current staging manifest.\n' >&2
  exit 1
fi
if ! jq -e --arg sha "$CANDIDATE_MANIFEST_SHA" --arg fixtureSha "$FIXTURE_SHA" '
    .status == "complete" and .bundleManifestSha256 == $sha and
    .fixtureSha256 == $fixtureSha and
    .executedCount == .passedCount and .skippedCount == 0
  ' "$DOTNET_REPORT" >/dev/null; then
  printf 'FAILED: .NET parity report does not validate the current staging manifest.\n' >&2
  exit 1
fi
if ! jq -e '.status == "candidate-staged" and .profile == "multilingual"' \
  "$STAGING_ROOT/laya-bundle-manifest.json" >/dev/null; then
  printf 'FAILED: staging manifest is not a multilingual candidate.\n' >&2
  exit 1
fi

mkdir -p "$PUBLISH_ROOT" "$(dirname "$CURRENT_POINTER")"
readonly VERSION_ROOT="$PUBLISH_ROOT/$CANDIDATE_MANIFEST_SHA"
if [[ -e "$VERSION_ROOT" ]]; then
  printf 'FAILED: refusing to overwrite existing version: %s\n' "$VERSION_ROOT" >&2
  exit 1
fi

readonly TEMP_ROOT="$(mktemp -d "$PUBLISH_ROOT/.publish.XXXXXX")"
# 發布失敗時清理未完成的暫存 bundle，不觸碰目前 pointer。
cleanup() {
  if [[ -d "$TEMP_ROOT" ]]; then
    rm -rf "$TEMP_ROOT"
  fi
}
trap cleanup EXIT

cp -a "$STAGING_ROOT/." "$TEMP_ROOT/"
readonly PUBLISHED_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
jq --arg publishedUtc "$PUBLISHED_UTC" --arg candidateSha "$CANDIDATE_MANIFEST_SHA" \
  '.status = "verified" | .publication = {candidateManifestSha256: $candidateSha, publishedUtc: $publishedUtc}' \
  "$TEMP_ROOT/laya-bundle-manifest.json" > "$TEMP_ROOT/laya-bundle-manifest.json.tmp"
mv "$TEMP_ROOT/laya-bundle-manifest.json.tmp" "$TEMP_ROOT/laya-bundle-manifest.json"
readonly VERIFIED_MANIFEST_SHA="$(sha256sum "$TEMP_ROOT/laya-bundle-manifest.json" | cut -d' ' -f1)"

mv "$TEMP_ROOT" "$VERSION_ROOT"
trap - EXIT
readonly POINTER_TEMP="$(mktemp "$(dirname "$CURRENT_POINTER")/.current.XXXXXX")"
jq -n \
  --arg root "$VERSION_ROOT" \
  --arg manifest "$VERSION_ROOT/laya-bundle-manifest.json" \
  --arg candidateSha "$CANDIDATE_MANIFEST_SHA" \
  --arg verifiedSha "$VERIFIED_MANIFEST_SHA" \
  --arg publishedUtc "$PUBLISHED_UTC" \
  '{schemaVersion: 1, status: "verified", profile: "multilingual", root: $root,
    manifest: $manifest, candidateManifestSha256: $candidateSha,
    verifiedManifestSha256: $verifiedSha, publishedUtc: $publishedUtc}' > "$POINTER_TEMP"
mv "$POINTER_TEMP" "$CURRENT_POINTER"
printf '{"status":"verified","root":"%s","manifestSha256":"%s"}\n' \
  "$VERSION_ROOT" "$VERIFIED_MANIFEST_SHA"
