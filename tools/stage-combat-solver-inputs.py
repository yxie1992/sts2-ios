#!/usr/bin/env python3
"""Copy the user's installed inputs into the ignored solver build area."""
import argparse
import os
import shutil
import tempfile
from pathlib import Path


def copy(source: Path, destination: Path) -> None:
    # Check every output ancestor before the same-file shortcut. A nested game/
    # or ritsu/ symlink must never turn this staging tool into a Steam writer.
    if destination.resolve() != destination:
        raise ValueError(f"Refusing symlinked output or output ancestor: {destination}")
    if source.resolve() == destination.resolve():
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    original = source.stat()
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=destination.parent, prefix=".solver-stage-", delete=False) as stream:
            temporary = Path(stream.name)
        shutil.copy2(source, temporary)
        current = source.stat()
        if (original.st_ino, original.st_size, original.st_mtime_ns) != (current.st_ino, current.st_size, current.st_mtime_ns):
            raise ValueError(f"Input changed while being copied; retry after the update finishes: {source}")
        # Replacing the directory entry also avoids writing through an existing
        # destination hard link into another installation.
        os.replace(temporary, destination)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-resources", required=True, type=Path)
    parser.add_argument("--workshop", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    target = root / "ios-export/.work/solver-inputs"
    if target.resolve() != target:
        raise ValueError("Refusing symlinked build input directory")

    for name in ["Slay the Spire 2.pck", "icon.icns", "release_info.json"]:
        copy(args.game_resources / name, target / "game" / name)
    for path in (args.game_resources / "data_sts2_macos_arm64").glob("*.dll"):
        copy(path, target / "game/data_sts2_macos_arm64" / path.name)
    for item, destination in [("3790899961", "desktop"), ("3747602295", "ritsu")]:
        source = args.workshop / item
        if not source.is_dir():
            raise ValueError(f"Required installed Workshop item missing: {source}")
        for path in source.rglob("*"):
            if path.is_symlink():
                raise ValueError(f"Unexpected symlink in Workshop input: {path}")
            if path.is_file():
                copy(path, target / destination / path.relative_to(source))
    print("Copied game and installed mods into isolated solver inputs; no Steam files were written.")


if __name__ == "__main__":
    main()
