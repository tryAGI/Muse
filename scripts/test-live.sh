#!/usr/bin/env bash
set -euo pipefail
set +x
ROOT="$(dirname "$(dirname "$(realpath "$0")")")"
dotnet build "$ROOT/Muse.slnx" -c Release --nologo
# Parse only known assignments; never source or evaluate the credential file.
if [[ -f "$ROOT/.env" ]]; then
  while IFS='=' read -r key value || [[ -n "$key" ]]; do
    case "$key" in
      MUSE_SDK_TOKEN|MUSE_DEVICE_ACCESS_TOKEN|MUSE_DEVICE_REFRESH_TOKEN|MUSE_DEVICE_ID|MUSE_VM_ID|MUSE_VM_AUTH_TOKEN|MUSE_NOISE_HOST)
        if [[ -z "${!key:-}" ]]; then export "$key=$value"; fi ;;
    esac
  done < "$ROOT/.env"
fi
case "${1:-live}" in
  live) FILTER='TestCategory=Live' ;;
  probe-sdk-token)
    FILTER='TestCategory=LiveProbe'
    python3 "$ROOT/scripts/probe-python-auth.py" --live
    export MUSE_PYTHON_AUTH_REPORT="$ROOT/artifacts/auth-python.json"
    ;;
  *) printf 'Usage: bash scripts/test-live.sh [live|probe-sdk-token]\n' >&2; exit 2 ;;
esac
mkdir -p "$ROOT/artifacts"
RESULTS="$(mktemp -d "$ROOT/artifacts/live-auth-XXXXXX")"
printf 'Running the selected live test; credentials remain redacted.\n'
dotnet test "$ROOT/Muse.slnx" -c Release --no-build --no-restore --filter "$FILTER" \
  --results-directory "$RESULTS" --logger 'console;verbosity=normal' --logger 'trx;LogFileName=live.trx'
python3 "$ROOT/scripts/check-live-results.py" "$RESULTS/live.trx"
