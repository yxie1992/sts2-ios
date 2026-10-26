"""Small regression checks for staging isolation and exact beta input guards."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


def load(name):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parents[1] / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


stage = load("stage-combat-solver-inputs")
prepare = load("prepare-combat-solver")


class SolverInputTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()

    def test_nested_output_symlink_is_rejected_before_copy(self):
        outside = self.root / "outside"
        outside.mkdir()
        source = self.root / "original"
        source.write_text("original")
        staging = self.root / "staging"
        staging.mkdir()
        (staging / "game").symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "ancestor"):
            stage.copy(source, staging / "game/sts2.dll")
        self.assertEqual(list(outside.iterdir()), [])

    def test_same_file_shortcut_does_not_accept_symlinked_output(self):
        outside = self.root / "outside"
        outside.mkdir()
        source = outside / "sts2.dll"
        source.write_text("original")
        alias = self.root / "alias"
        alias.symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "ancestor"):
            stage.copy(source, alias / "sts2.dll")
        self.assertEqual(source.read_text(), "original")

    def test_existing_hardlink_is_replaced_without_writing_through_it(self):
        original = self.root / "outside"
        original.write_text("outside")
        destination = self.root / "staged"
        destination.hardlink_to(original)
        source = self.root / "input"
        source.write_text("new")
        stage.copy(source, destination)
        self.assertEqual(original.read_text(), "outside")
        self.assertEqual(destination.read_text(), "new")

    def game_fixture(self):
        data = self.root / "game/data_sts2_macos_arm64"
        data.mkdir(parents=True)
        (data / "sts2.dll").write_bytes(b"game")
        (self.root / "game/Slay the Spire 2.pck").write_bytes(b"assets")
        (self.root / "game/release_info.json").write_text(json.dumps({"version": "v0.111.0"}))
        return {"version": "0.111.0", "sha256": hashlib.sha256(b"game").hexdigest(), "pckSha256": hashlib.sha256(b"assets").hexdigest()}

    def test_matching_game_and_pack_are_accepted(self):
        hashes = prepare.validate_game_inputs(self.root, self.game_fixture())
        self.assertEqual(len(hashes), 3)

    def test_mixed_pack_is_rejected(self):
        pin = self.game_fixture()
        (self.root / "game/Slay the Spire 2.pck").write_bytes(b"old assets")
        with self.assertRaisesRegex(ValueError, "mixed game versions"):
            prepare.validate_game_inputs(self.root, pin)

    def test_wrong_game_dll_is_rejected(self):
        pin = self.game_fixture()
        (self.root / "game/data_sts2_macos_arm64/sts2.dll").write_bytes(b"different game")
        with self.assertRaisesRegex(ValueError, "pinned original"):
            prepare.validate_game_inputs(self.root, pin)

    def test_wrong_release_version_is_rejected(self):
        pin = self.game_fixture()
        (self.root / "game/release_info.json").write_text(json.dumps({"version": "v0.107.1"}))
        with self.assertRaisesRegex(ValueError, "release_info version"):
            prepare.validate_game_inputs(self.root, pin)


if __name__ == "__main__":
    unittest.main()
