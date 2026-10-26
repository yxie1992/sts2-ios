#!/usr/bin/env bash
# Build host-only generators and the statically adapted solver. No deployment.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
source "$SCRIPT_DIR/config.sh"
DOTNET="${STS2_DOTNET_ROOT:-$HOME/.dotnet}/dotnet"
WORK="$SCRIPT_DIR/.work/solver-inputs"
SOURCES="$SCRIPT_DIR/.work/solver-sources"
export DOTNET_ROOT="${STS2_DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_PROCESSOR_COUNT="${DOTNET_PROCESSOR_COUNT:-1}"
export DOTNET_CLI_USE_MSBUILD_SERVER=0
export MSBUILDDISABLENODEREUSE=1
mkdir -p "$WORK" "$SOURCES"

python3 "$ROOT/tools/stage-combat-solver-inputs.py" \
  --game-resources "$STS2_GAME_APP" \
  --workshop "${STS2_WORKSHOP_DIR:-$HOME/Library/Application Support/Steam/steamapps/workshop/content/2868840}"

# Only public mod source is downloaded. Game binaries come from the user's install.
SOLVER_SOURCE="${STS2_COMBAT_SOLVER_SOURCE:-$SOURCES/CombatSolver}"
RITSU_SOURCE="${STS2_RITSU_SOURCE:-$SOURCES/RitsuLib}"
if [ ! -d "$SOLVER_SOURCE/.git" ]; then
  git clone --quiet https://github.com/Torch1230/CombatSolver.git "$SOLVER_SOURCE"
  git -C "$SOLVER_SOURCE" checkout --quiet --detach 5c773caaec742bc55588bc9ee21c361326a732f0
fi
if [ ! -d "$RITSU_SOURCE/.git" ]; then
  git clone --quiet https://github.com/BAKAOLC/STS2-RitsuLib.git "$RITSU_SOURCE"
  git -C "$RITSU_SOURCE" checkout --quiet --detach 772f280964f2efc7c7e2f29fc59844e68616dd57
fi
python3 "$ROOT/tools/prepare-combat-solver.py" \
  --combat-solver-source "$SOLVER_SOURCE" --ritsu-source "$RITSU_SOURCE"

"$DOTNET" build "$ROOT/tools/CombatSolverAot" -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false
"$DOTNET" build "$ROOT/tools/CombatSolverDispatch" -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false
CORE="$ROOT/tools/CombatSolverAot/bin/Release/net9.0/CombatSolverAot.dll"
"$DOTNET" "$CORE" bridge "$WORK/game/data_sts2_macos_arm64/sts2.dll" "$WORK/bridged/sts2.dll" "$WORK/bridged/report.json"
"$DOTNET" "$CORE" recipes "$WORK/desktop/CombatSolver.dll" "$WORK/game/data_sts2_macos_arm64" \
  "$WORK/ritsu" "$WORK/generated/CardOnPlayRecipes.g.cs" "$WORK/generated/card-recipes.json"
"$DOTNET" "$ROOT/tools/CombatSolverDispatch/bin/Release/net9.0/CombatSolverDispatch.dll" \
  "$WORK/game/data_sts2_macos_arm64/sts2.dll" "$WORK/generated"
python3 "$ROOT/tools/patch-combat-solver-source.py"
python3 "$ROOT/tools/patch-combat-solver-json.py"
"$DOTNET" build "$ROOT/src/CombatSolverIos" -c Release -m:1 --disable-build-servers -p:UseSharedCompilation=false
"$DOTNET" "$CORE" rewrite "$ROOT/src/CombatSolverIos/bin/Release/net9.0/CombatSolver.dll" \
  "$WORK/bridged/runtime/sts2.dll" "$WORK/rewritten/CombatSolver.dll" "$WORK/rewritten/sts2.dll" \
  "$WORK/rewritten/access-report.json" "$WORK/game/data_sts2_macos_arm64"
# Adjacent original references allow the existing read-only Weaver to resolve metadata.
for dll in "$WORK/game/data_sts2_macos_arm64/"*.dll; do
  [ "$(basename "$dll")" = "sts2.dll" ] || cp "$dll" "$WORK/rewritten/"
done
for dll in compat/0.111.0/STS2-RitsuLib.dll compat/0.111.0/STS2-RitsuLib.Runtime.dll \
  shared/STS2-RitsuLib.Shared.dll shared/STS2-RitsuLib.Ui.dll shared/STS2-RitsuLib.Settings.dll; do
  cp "$WORK/ritsu/$dll" "$WORK/rewritten/"
done
echo "Adapted solver and runtime game assemblies prepared; original game member visibility preserved."
