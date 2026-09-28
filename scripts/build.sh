#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_BIN="${DOTNET:-dotnet}"
if ! command -v "$DOTNET_BIN" >/dev/null 2>&1 && [[ -x /Volume1/@apps/jellyfin/dotnet/dotnet ]]; then DOTNET_BIN=/Volume1/@apps/jellyfin/dotnet/dotnet; fi
"$DOTNET_BIN" build "$ROOT/Jellyfin.Plugin.DongmanYaa.sln" --configuration Release --nologo
