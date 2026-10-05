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
  probe-sdk-token) FILTER='TestCategory=LiveProbe' ;;
  *) printf 'Usage: bash scripts/test-live.sh [live|probe-sdk-token]\n' >&2; exit 2 ;;
esac
printf 'Running the selected live test; credentials remain redacted.\n'
dotnet test "$ROOT/Muse.slnx" -c Release --no-build --no-restore --filter "$FILTER" \
  --logger 'console;verbosity=normal' --logger 'trx;LogFileName=live.trx'
