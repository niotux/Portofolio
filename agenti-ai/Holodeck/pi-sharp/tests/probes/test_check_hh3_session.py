import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "tools/probes/check-hh3-session.py"
SPEC = importlib.util.spec_from_file_location("check_hh3_session", SCRIPT)
CHECKER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECKER)


class Hh3SessionCheckerTests(unittest.TestCase):
    def test_accepts_large_marker_only_when_referenced_artifact_is_complete(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "tool-output.txt"
            full_text = "x" * 40_004
            output.write_text(full_text, encoding="utf-8")
            session = root / "session.jsonl"
            marker = (f"[...] 1 tokens omitted; original 10001 tokens, 1 lines; "
                      f"full output: {output}]")
            session.write_text(json.dumps({"content": marker}) + "\n", encoding="utf-8")

            self.assertIn("HH3 PASS", CHECKER.verify(session))

    def test_rejects_missing_or_incomplete_artifact(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "tool-output.txt"
            output.write_text("too short", encoding="utf-8")
            session = root / "session.jsonl"
            marker = (f"[...] 1 tokens omitted; original 10001 tokens, 1 lines; "
                      f"full output: {output}]")
            session.write_text(json.dumps({"content": marker}) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "no valid"):
                CHECKER.verify(session)


if __name__ == "__main__":
    unittest.main()
