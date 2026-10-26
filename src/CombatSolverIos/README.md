# Statically linked CombatSolver for iOS

This project compiles the pinned CombatSolver source into the native iOS build.
It is not a general mod loader. Its managed DLL is an intermediate artifact;
the access-bridge rewrite, static mobile hooks and NativeAOT publish are required
before deployment. A managed compile alone does not establish device correctness.

`upstream.json` pins CombatSolver 0.50.1, RitsuLib 0.6.10 and the user's original
0.111.0 beta game DLL and PCK hashes. Preparation also checks the release-info
version. Sources and proprietary binaries are kept in ignored
`ios-export/.work/solver-inputs/`. The preparation tool requires clean pinned
source checkouts and copies tracked files; it never imports upstream project
files, `local.props`, or their automatic Steam deployment targets.

The prepared input layout is:

```
solver-inputs/
  game/data_sts2_macos_arm64/   # User's copied original beta DLLs
  desktop/                    # User's copied installed CombatSolver + manifest
  ritsu/compat/0.111.0/        # Copied Ritsu compatibility assemblies
  ritsu/shared/               # Copied Ritsu shared assemblies
  source/                     # Pinned sources copied by preparation
  generated/                  # Static recipes, dispatch and checked overlays
  bridged/sts2.dll            # Compile-only reference, never deployed
  bridged/runtime/sts2.dll    # Original visibility plus static clone bridges
  licenses/                   # Upstream notices copied beside build outputs
  prepared.json               # Source pins and content hashes
```

Prepare using the two local source checkout paths, then use the static generator
tools before compiling this project. All writes remain within this checkout:

```sh
python3 tools/prepare-combat-solver.py \
  --combat-solver-source /path/to/CombatSolver \
  --ritsu-source /path/to/STS2-RitsuLib

# After CombatSolverAot and CombatSolverDispatch generate their outputs:
DOTNET_PROCESSOR_COUNT=1 DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 \
  MSBUILDDISABLENODEREUSE=1 nice -n 15 "$HOME/.dotnet/dotnet" build \
  src/CombatSolverIos/CombatSolverIos.csproj -c Release \
  -p:UseSharedCompilation=false -m:1
```

Each build verifies the copied input hashes. Preparation refuses unexpected
source revisions, modified tracked source, incorrect beta/Workshop versions,
mixed game/asset versions and symlinked staging outputs. Re-prepare only when intentionally restoring the
unchanged pinned inputs. No script here writes the active Steam game, launches
the game, synchronizes saves or installs an app.

The mobile dependency adapters enforce the initial original-content scope:

- Loaded mods, external model types, native-card external attachments and any
  ModHelper subscriber explicitly reject a search.
- Runtime Harmony patch-table checks are replaced by that fixed-build contract.
  Runtime card/monster replacement adapters are unsupported.
- Listener filtering retains the complete dispatch sequence, and optional clone
  concurrency optimization remains disabled. No event or unknown effect is
  silently dropped to make a search succeed.
- The hand limit comes from the native `CardPile.MaxCardsInHand` getter, verified
  as `10` in the pinned original beta assembly. Ritsu's desktop framework is not
  initialized to retrieve the limit.
- Ritsu compatibility/shared assemblies remain real compile dependencies. Full
  desktop entry, Workshop integration and patch installation are not invoked.

Search keys, settings, the diagnostic journal and replay evidence use generated
System.Text.Json metadata from `Adapters/MobileSolverJson.cs`. Numeric-enum search
keys keep the upstream default options; evidence keeps string enums and relaxed
escaping; settings keep camel-case names, indentation, string enums and legacy
read fields. These calls are evaluated even when a diagnostic sink is disabled,
so omitting reflection metadata alone would break real searches on NativeAOT.
`tools/patch-combat-solver-json.py` emits seven compiler overlays and a settings
read DTO after checking each pinned file's SHA256 and exact replacement counts.
The DTO preserves all 62 upstream default values and migration setters, then
copies to the original immutable settings record. This avoids .NET 9 source
generation overwriting omitted init-only properties with zero/null during sparse
settings reads. The project excludes
the originals and regenerates its owned `generated/Json/` files before compile;
unexpected stale files are rejected. The isolated host harness compares all
converted serialization shapes byte-for-byte against upstream reflection JSON.

CombatSolver and RitsuLib are MIT-licensed. CombatSolver also carries required
Random Foreseer attribution and permission notices. Preparation retains all of
these, and this project copies them into its output `licenses/` directory.
Keep that directory with every local packaged solver build.
