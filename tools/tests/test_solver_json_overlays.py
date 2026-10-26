"""Regression checks for fail-closed JSON overlay generation; no game required."""
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location(
    "solver_json", Path(__file__).parents[1] / "patch-combat-solver-json.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SolverJsonOverlayTests(unittest.TestCase):
    def test_replacement_rejects_missing_or_duplicate_calls(self):
        for text in ("different", "call call"):
            with self.assertRaisesRegex(ValueError, "Pinned JSON source changed"):
                module.replace(text, "call", "generated")

    def test_input_hash_drift_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory)
            (source / "input.cs").write_text("changed")
            with patch.object(module, "SOURCE", source), patch.object(module, "SOURCE_SHA256",
                    {"input.cs": hashlib.sha256(b"pinned").hexdigest()}):
                with self.assertRaisesRegex(ValueError, "hash mismatch"):
                    module.read_source("input.cs")

    def test_output_symlink_rejected_before_generating(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve()
            outside = root / "outside"
            outside.mkdir()
            output = root / "output"
            output.symlink_to(outside, target_is_directory=True)
            with patch.object(module, "DESTINATION", output):
                with self.assertRaisesRegex(ValueError, "symlinked"):
                    module.main()
            self.assertEqual(list(outside.iterdir()), [])

    def test_stale_outputs_rejected_instead_of_being_compiled(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory).resolve()
            (output / "stale.cs").write_text("do not compile")
            with patch.object(module, "DESTINATION", output), patch.object(module, "overlays", return_value={"A.cs": "class A {}"}):
                with self.assertRaisesRegex(ValueError, "Unexpected files"):
                    module.main()
            self.assertFalse((output / "A.Json.g.cs").exists())


if __name__ == "__main__":
    unittest.main()
