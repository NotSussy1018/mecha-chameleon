#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_PATH="${UNITY_PATH:-/Applications/Unity/Hub/Editor/6000.1.1f1/Unity.app/Contents/MacOS/Unity}"
ENVIRONMENT="${1:-}"
MODE="${2:-build}"

case "$ENVIRONMENT" in
  development)
    BUILD_METHOD="MechaChameleon.Editor.DeploymentBuild.BuildDevelopment"
    VALIDATE_METHOD="MechaChameleon.Editor.DeploymentBuild.ValidateDevelopment"
    ;;
  staging)
    BUILD_METHOD="MechaChameleon.Editor.DeploymentBuild.BuildStaging"
    VALIDATE_METHOD="MechaChameleon.Editor.DeploymentBuild.ValidateStaging"
    ;;
  production)
    BUILD_METHOD="MechaChameleon.Editor.DeploymentBuild.BuildProduction"
    VALIDATE_METHOD="MechaChameleon.Editor.DeploymentBuild.ValidateProduction"
    ;;
  *)
    echo "usage: scripts/unity-build.sh development|staging|production [build|validate] [Unity arguments...]" >&2
    exit 64
    ;;
esac

METHOD="$BUILD_METHOD"
if [[ "$MODE" == "validate" ]]; then
  METHOD="$VALIDATE_METHOD"
elif [[ "$MODE" != "build" ]]; then
  echo "second argument must be build or validate" >&2
  exit 64
fi

if [[ ! -x "$UNITY_PATH" ]]; then
  echo "Unity not found at: $UNITY_PATH" >&2
  echo "Set UNITY_PATH to the Unity 6000.1.1f1 executable." >&2
  exit 69
fi

mkdir -p "$PROJECT_ROOT/Logs"
LOG_PATH="$PROJECT_ROOT/Logs/${ENVIRONMENT}-${MODE}.log"
if (( $# > 0 )); then shift; fi
if (( $# > 0 )); then shift; fi

echo "Unity ${MODE}: ${ENVIRONMENT}"
echo "Log: $LOG_PATH"

if ! "$UNITY_PATH" \
  -batchmode \
  -nographics \
  -quit \
  -projectPath "$PROJECT_ROOT" \
  -executeMethod "$METHOD" \
  -logFile "$LOG_PATH" \
  "$@"; then
  tail -80 "$LOG_PATH" >&2 || true
  exit 1
fi

grep '\[DeploymentBuild\]' "$LOG_PATH" | tail -10 || tail -20 "$LOG_PATH"
