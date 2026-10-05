#!/usr/bin/env bash
set -euo pipefail
ROOT="$(dirname "$(dirname "$(realpath "$0")")")"
# Root documents are the only maintained source; site copies are generated.
cp "$ROOT/README.md" "$ROOT/docs/index.md"
cp "$ROOT/PROTOCOL.md" "$ROOT/docs/PROTOCOL.md"
cp "$ROOT/VALIDATION.md" "$ROOT/docs/VALIDATION.md"
dotnet tool restore --tool-manifest "$ROOT/.config/dotnet-tools.json"
dotnet tool run autosdk docs sync "$ROOT"
# Retain exact identity even when the documentation generator updates examples.
cp "$ROOT/README.md" "$ROOT/docs/index.md"
