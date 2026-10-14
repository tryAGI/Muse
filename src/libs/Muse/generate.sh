#!/usr/bin/env bash
set -euo pipefail
# The schema is maintained locally from pinned public protocol evidence.
# Run from any working directory. Never download over the source schema.
ROOT="$(dirname "$(realpath "$0")")"
dotnet tool restore --tool-manifest "$ROOT/../../../.config/dotnet-tools.json"
dotnet tool run autosdk generate "$ROOT/openapi.yaml" \
  --namespace Muse \
  --clientClassName MuseClient \
  --targetFramework net10.0 \
  --output "$ROOT/Generated" \
  --exclude-deprecated-operations
