#!/usr/bin/env bash
set -euo pipefail
ROOT="$(dirname "$(dirname "$(realpath "$0")")")"
# Keep compilation outside the live oracle's bounded connection lifetime.
if [[ $# -eq 0 ]]; then
  dotnet build "$ROOT/Muse.slnx" -c Release --nologo
fi
python3 -m venv "$ROOT/artifacts/oracle-venv"
"$ROOT/artifacts/oracle-venv/bin/python" -m pip install --disable-pip-version-check -q -r "$ROOT/scripts/oracle-requirements.txt"
"$ROOT/artifacts/oracle-venv/bin/python" "$ROOT/scripts/run-interop.py" "$@"
