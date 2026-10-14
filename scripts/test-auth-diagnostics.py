#!/usr/bin/env python3
"""Offline regression tests for the explicit live-result gate."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("live_gate", Path(__file__).with_name("check-live-results.py"))
live_gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live_gate)


class LiveGateTests(unittest.TestCase):
    def test_only_executed_successful_tests_pass(self):
        for outcomes, expected in [(["Passed"], 0), (["Passed", "Passed"], 0),
                                   ([], 3), (["NotExecuted"], 3), (["Passed", "NotExecuted"], 3),
                                   (["Failed"], 3), (["Inconclusive"], 3)]:
            with self.subTest(outcomes=outcomes), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "result.trx"
                body = "".join(f'<UnitTestResult outcome="{value}" />' for value in outcomes)
                path.write_text(f'<TestRun xmlns="urn:fixture"><Results>{body}</Results></TestRun>')
                self.assertEqual(live_gate.check(path), expected)

    def test_missing_and_malformed_evidence_fail(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            self.assertEqual(live_gate.check(path), 3)
            path.write_text("broken xml")
            self.assertEqual(live_gate.check(path), 3)


if __name__ == "__main__":
    unittest.main()
