#!/usr/bin/env python3
"""Fail an explicit live verification when no tests ran or any were skipped."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def check(path: Path) -> int:
    try:
        results = ET.parse(path).findall(".//{*}UnitTestResult")
    except (OSError, ET.ParseError):
        print("Live verification is incomplete: missing or malformed test evidence.")
        return 3
    passed = sum(result.get("outcome") == "Passed" for result in results)
    if not results or passed != len(results):
        print(f"Live verification is incomplete: {passed}/{len(results)} tests passed. A skipped/inconclusive test is not authenticated E2E.")
        return 3
    print(f"Selected live checks passed: {passed}. Their scope, not the runner exit code, defines what was verified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(check(Path(sys.argv[1])))
