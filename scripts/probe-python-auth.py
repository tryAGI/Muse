#!/usr/bin/env python3
"""Measure SDK-token discovery using the pinned upstream Python API function.

Development-only diagnostic, not a login helper or a proof of token validity.
The API module is unmodified. Only its package-version import is supplied as
'auth-diagnostic' to avoid installing the unrelated BLE/device executor package.
"""
import argparse
import hashlib
import json
import logging
import os
from pathlib import Path
import subprocess
import sys
import types
import urllib.request
from datetime import datetime, timezone

REVISION = "74a5e2d7fc895f109f83a9a1dbed705dbcd8b1ff"
SOURCE_BLOB = "d93e1fffdfdbb415720577c3ee1aaa8186d3d786"
ROOT = Path(__file__).resolve().parents[1]


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        # Never forward a credential to a different endpoint.
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live", action="store_true", required=True)
    parser.parse_args()
    token = os.environ.get("MUSE_SDK_TOKEN", "")
    if not token.startswith("mgst_") or any(ch.isspace() or ord(ch) < 32 for ch in token):
        print("SDK-token diagnostic requires MUSE_SDK_TOKEN without whitespace.", file=sys.stderr)
        return 2
    source = subprocess.run([
        "gh", "api", f"repos/facebookincubator/muse-gadget-sdk/contents/linux/src/musegadget/muse_api.py?ref={REVISION}",
        "-H", "Accept: application/vnd.github.raw+json",
    ], check=True, capture_output=True, timeout=20).stdout
    blob = hashlib.sha1(b"blob " + str(len(source)).encode() + b"\0" + source).hexdigest()
    if blob != SOURCE_BLOB:
        raise ValueError("Upstream source integrity mismatch")
    package = types.ModuleType("musegadget")
    package.__version__ = "auth-diagnostic"
    sys.modules["musegadget"] = package
    api = types.ModuleType("musegadget.muse_api")
    exec(compile(source, "pinned_muse_api.py", "exec"), api.__dict__)
    logging.disable(logging.CRITICAL)
    urllib.request.install_opener(urllib.request.build_opener(NoRedirect()))
    vms, status = api.fetch_vms_with_status(token)
    report = {
        "observedAtUtc": datetime.now(timezone.utc).isoformat(),
        "implementation": "unmodified upstream fetch_vms_with_status",
        "revision": REVISION, "sourceGitBlob": blob,
        "packageVersionMetadata": "auth-diagnostic (not an upstream release version)",
        "userAgent": api.user_agent(), "apiVersion": "1.0.0",
        "authorizationScheme": "Bearer", "credentialRole": "gadget_sdk",
        "tlsValidation": True, "redirects": False, "httpStatus": status,
        "vmCount": len(vms), "sdkTokenValidityForPairing": "not_tested",
        "authenticatedChat": "not_exercised",
    }
    output = ROOT / "artifacts" / "auth-python.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))
    # Success here means a measurement was obtained, NOT account authorization.
    # test-live.sh separately rejects skipped/inconclusive C# authentication tests.
    return 0 if status is not None else 4


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        # Do not print arbitrary provider data, request objects or credentials.
        print(f"Python authentication diagnostic failed: {type(error).__name__}", file=sys.stderr)
        raise SystemExit(4) from None
