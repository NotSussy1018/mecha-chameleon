#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.1.1f1/Unity.app/Contents/MacOS/Unity}"
PLATFORM="${1:-all}"

if [[ ! -x "$UNITY_PATH" ]]; then
  echo "Unity not found at: $UNITY_PATH" >&2
  exit 69
fi

mkdir -p "$PROJECT_ROOT/Logs"

run_tests() {
  local platform="$1"
  local platform_lower
  platform_lower="$(printf '%s' "$platform" | tr '[:upper:]' '[:lower:]')"
  local result_path="$PROJECT_ROOT/Logs/${platform_lower}-results.xml"
  local log_path="$PROJECT_ROOT/Logs/${platform_lower}-tests.log"

  echo "Unity tests: $platform"
  "$UNITY_PATH" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_ROOT" \
    -runTests \
    -testPlatform "$platform" \
    -testResults "$result_path" \
    -logFile "$log_path"

  head -2 "$result_path"
}

case "$PLATFORM" in
  edit|EditMode) run_tests EditMode ;;
  play|PlayMode) run_tests PlayMode ;;
  all)
    run_tests EditMode
    run_tests PlayMode
    ;;
  *)
    echo "usage: scripts/unity-test.sh edit|play|all" >&2
    exit 64
    ;;
esac
