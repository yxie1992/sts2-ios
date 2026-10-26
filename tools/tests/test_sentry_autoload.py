import struct
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from pck_patch_sentry import disable_sentry_autoload


def settings(records):
    result = b"ECFG" + struct.pack("<I", len(records))
    for name, value in records:
        key = name.encode()
        result += struct.pack("<I", len(key)) + key + struct.pack("<I", len(value)) + value
    return result


class SentryAutoloadTests(unittest.TestCase):
    def test_known_game_versions_preserve_offsets_and_other_settings(self):
        for key in ["autoload/SentryInit", "autoload/SentryBootstrap"]:
            with self.subTest(key=key):
                original = settings([("description", b"autoload/SentryInit"),
                                     (key, b"*res://addons/sentry/start.cs"),
                                     ("autoload/FmodManager", b"fmod")])
                output, removed = disable_sentry_autoload(original)
                expected = settings([("description", b"autoload/SentryInit"),
                                     ("x" + key[1:], b"*res://addons/sentry/start.cs"),
                                     ("autoload/FmodManager", b"fmod")])
                self.assertEqual(output, expected)
                self.assertEqual(removed, [key])
                self.assertEqual(len(output), len(original))

    def test_unknown_sentry_entry_is_not_silently_accepted(self):
        with self.assertRaisesRegex(AssertionError, "requires review"):
            disable_sentry_autoload(settings([("autoload/NewTelemetry", b"res://addons/sentry/new.cs")]))

    def test_value_only_match_does_not_count_as_autoload(self):
        with self.assertRaisesRegex(AssertionError, "not found"):
            disable_sentry_autoload(settings([("description", b"autoload/SentryInit")]))

    def test_truncated_value_is_rejected(self):
        with self.assertRaisesRegex(AssertionError, "truncated"):
            disable_sentry_autoload(settings([("autoload/SentryInit", b"abcd")])[:-1])


if __name__ == "__main__":
    unittest.main()
