#!/usr/bin/env python3
"""Stage pinned open source and validate already-copied local solver inputs.

No game process is launched, and no path in Steam is written. Game and Workshop
binaries must already be copied under the repository's ignored solver-inputs.
Source repositories are only read; upstream build/deployment targets never run.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path


def git(source: Path, *args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(source), *args])


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def validate_game_inputs(destination: Path, pin: dict) -> dict[str, str]:
    game = destination / "game/data_sts2_macos_arm64/sts2.dll"
    pack = destination / "game/Slay the Spire 2.pck"
    release = destination / "game/release_info.json"
    hashes = {}
    for path in (game, pack, release):
        if path.resolve() != path:
            raise ValueError(f"Copied game input is symlinked: {path}")
        hashes[str(path.relative_to(destination))] = sha256(path)
    if hashes[str(game.relative_to(destination))] != pin["sha256"]:
        raise ValueError("Copied beta sts2.dll does not match the pinned original; do not use a woven DLL here")
    if hashes[str(pack.relative_to(destination))] != pin["pckSha256"]:
        raise ValueError("Copied game PCK does not match the pinned beta assets; refusing mixed game versions")
    version = json.loads(release.read_text(encoding="utf-8-sig"))["version"]
    if version not in (pin["version"], "v" + pin["version"]):
        raise ValueError(f"Copied release_info version is {version}, expected {pin['version']}")
    return hashes


def verify(destination: Path, pins: dict) -> None:
    manifest = json.loads((destination / "prepared.json").read_text())
    if manifest["pins"] != pins:
        raise ValueError("Prepared source pins have changed; prepare inputs again")
    for relative, expected in manifest["hashes"].items():
        path = destination / relative
        if path.resolve() != path or sha256(path) != expected:
            raise ValueError(f"Prepared input changed: {relative}")
    actual = {str(p.relative_to(destination)) for p in (destination / "source/CombatSolver").rglob("*.cs")}
    expected = {p for p in manifest["hashes"] if p.startswith("source/CombatSolver/") and p.endswith(".cs")}
    if actual != expected:
        raise ValueError("Prepared CombatSolver source file set changed")
    print("Verified pinned CombatSolver sources and copied input hashes")


def checked_source(source: Path, pin: dict) -> list[Path]:
    if git(source, "rev-parse", "HEAD").decode().strip() != pin["commit"]:
        raise ValueError(f"{source}: expected pinned commit {pin['commit']}")
    if git(source, "status", "--porcelain", "--untracked-files=no").strip():
        raise ValueError(f"{source}: tracked source changes would invalidate the pin")
    paths = [Path(p.decode()) for p in git(source, "ls-files", "-z").split(b"\0") if p]
    for relative in paths:
        path = source / relative
        if path.is_symlink() or not path.is_file() or not path.resolve().is_relative_to(source):
            raise ValueError(f"Unsupported tracked input: {path}")
    return paths


def generate_listener_adapter(source: Path, destination: Path) -> Path:
    """Retain upstream dispatch unchanged, conservatively disable its fast path."""
    text = (source / "src/Engine/Common/MirroredHookListenerFilter.cs").read_text()
    capture = '''    internal static MirroredHookListenerFilter Capture()
        => new(!BaseHooks.Append(NativeKeywordHook).Any(method =>
            Harmony.GetPatchInfo(method) is { } patches && patches.Owners.Count != 0));'''
    replacement = '''    internal static MirroredHookListenerFilter Capture()
    {
        VanillaRuntimeContract.ValidateLoadedMods();
        // Preserve the complete dispatch list; no patch-table or metadata fast path.
        return new(false);
    }'''
    filter_entry = '''        ref MirroredHookListenerLayout? layout)
    {'''
    if text.count(capture) != 1 or text.count(filter_entry) != 1:
        raise ValueError("Pinned listener-filter source contract changed")
    text = text.replace("using HarmonyLib;\n", "")
    text = text.replace(capture, replacement)
    text = text.replace(filter_entry, filter_entry + "\n        VanillaRuntimeContract.ValidateModels(source);")
    output = destination / "generated/MirroredHookListenerFilter.Mobile.g.cs"
    if output.resolve() != output:
        raise ValueError("Refusing symlinked generated-source output")
    output.parent.mkdir(exist_ok=True)
    output.write_text("// Adapted from pinned CombatSolver (MIT). See output licenses.\n#nullable enable\n" + text)
    return output


def generate_continuation_adapter(source: Path, destination: Path) -> Path:
    text = (source / "src/Runtime/ContinuationStamp.cs").read_text()
    replacements = {
        "RitsuLibFramework.GetMaxHandSize(player)": "VanillaRuntimeContract.NativeMaxHandSize",
        "AdaptedCardOnPlayMirrors.CaptureLiveStamp()": "VanillaRuntimeContract.CaptureOnPlayConfiguration()",
    }
    for original, replacement in replacements.items():
        if text.count(original) != 1:
            raise ValueError(f"Pinned continuation-stamp source contract changed: {original}")
        text = text.replace(original, replacement)
    text = text.replace("using STS2RitsuLib;\n", "")
    output = destination / "generated/ContinuationStamp.Mobile.g.cs"
    if output.resolve() != output:
        raise ValueError("Refusing symlinked generated-source output")
    output.write_text("// Adapted from pinned CombatSolver (MIT). See output licenses.\n#nullable enable\n" + text)
    return output


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--combat-solver-source", type=Path)
    parser.add_argument("--ritsu-source", type=Path)
    parser.add_argument("--verify", action="store_true", help="Verify previously prepared input content without copying")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    destination = root / "ios-export/.work/solver-inputs"
    # Do not permit a symlinked .work directory to redirect writes into Steam.
    if destination.resolve() != destination:
        raise ValueError("solver-inputs and its parents must not be symlinks")
    pins = json.loads((root / "src/CombatSolverIos/upstream.json").read_text())
    if args.verify:
        verify(destination, pins)
        return
    if args.combat_solver_source is None or args.ritsu_source is None:
        parser.error("both source checkouts are required unless --verify is used")
    combat = args.combat_solver_source.resolve()
    ritsu = args.ritsu_source.resolve()
    sources = [(combat, "CombatSolver", pins["combatSolver"]), (ritsu, "RitsuLib", pins["ritsuLib"])]
    tracked = {name: checked_source(path, pin) for path, name, pin in sources}
    game_dir = destination / "game/data_sts2_macos_arm64"
    game = game_dir / "sts2.dll"
    game_hashes = validate_game_inputs(destination, pins["game"])
    required = [game_dir / name for name in ("GodotSharp.dll", "0Harmony.dll")]
    required += [destination / "ritsu/compat/0.111.0" / name for name in ("STS2-RitsuLib.dll", "STS2-RitsuLib.Runtime.dll")]
    required += [destination / "ritsu/shared" / f"STS2-RitsuLib.{name}.dll" for name in ("Shared", "Ui", "Settings")]
    required += [destination / "desktop/CombatSolver.dll"]
    for path in required:
        if not path.is_file() or path.resolve() != path:
            raise ValueError(f"Missing copied input or symlink: {path}")
    ritsu_manifest = json.loads((destination / "ritsu/mod_manifest.json").read_text(encoding="utf-8-sig"))
    solver_manifest = json.loads((destination / "desktop/CombatSolver.json").read_text(encoding="utf-8-sig"))
    if ritsu_manifest["version"] != pins["ritsuLib"]["version"] or solver_manifest["version"] != pins["combatSolver"]["version"]:
        raise ValueError("Copied Workshop versions do not match the pins")

    # Replace only our own previously staged source copy. Never edit an upstream
    # checkout, import its .props files, or run its automation.
    hashes = game_hashes | {str(path.relative_to(destination)): sha256(path) for path in required}
    for source, name, pin in sources:
        target = destination / "source" / name
        if target.resolve() != target:
            raise ValueError(f"Refusing symlinked staging output: {target}")
        if target.exists():
            if target.is_symlink():
                raise ValueError(f"Refusing symlinked staging output: {target}")
            shutil.rmtree(target)
        for relative in tracked[name]:
            output = target / relative
            output.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source / relative, output)
            hashes[str(output.relative_to(destination))] = sha256(output)
    licenses = destination / "licenses"
    if licenses.resolve() != licenses:
        raise ValueError("Refusing symlinked licenses output")
    licenses.mkdir(exist_ok=True)
    shutil.copy2(combat / "LICENSE", licenses / "CombatSolver-LICENSE.txt")
    shutil.copy2(combat / "THIRD_PARTY_NOTICES.md", licenses / "CombatSolver-THIRD_PARTY_NOTICES.md")
    shutil.copy2(ritsu / "LICENSE", licenses / "RitsuLib-LICENSE.txt")
    for path in licenses.iterdir():
        hashes[str(path.relative_to(destination))] = sha256(path)
    for adapter in [generate_listener_adapter(combat, destination), generate_continuation_adapter(combat, destination)]:
        hashes[str(adapter.relative_to(destination))] = sha256(adapter)
    manifest = {
        "pins": pins,
        "hashes": hashes,
        "sourceFiles": {name: len(paths) for name, paths in tracked.items()},
        "isolation": "Copies only. No upstream projects, Steam writes, or game execution.",
    }
    (destination / "prepared.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"Prepared pinned sources and validated {len(required) + 1} copied assemblies in {destination}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f"CombatSolver input preparation failed: {error}", file=sys.stderr)
        sys.exit(1)
