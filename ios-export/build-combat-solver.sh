#!/usr/bin/env bash
# Build the pinned beta game plus mobile solver without installing or launching it.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export STS2_ENABLE_COMBAT_SOLVER=1
export DOTNET_PROCESSOR_COUNT="${DOTNET_PROCESSOR_COUNT:-1}"
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
exec nice -n 15 bash "$SCRIPT_DIR/build-ios.sh"
