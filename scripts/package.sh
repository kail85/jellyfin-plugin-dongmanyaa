#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
"$ROOT/scripts/test.sh"
OUT="$ROOT/src/Jellyfin.Plugin.DongmanYaa/bin/Release/net9.0"
STAGE="$ROOT/artifacts/Jellyfin.Plugin.DongmanYaa_0.1.0.0"
mkdir -p "$STAGE"
cp "$OUT/Jellyfin.Plugin.DongmanYaa.dll" "$STAGE/"
python3 - "$STAGE/meta.json" <<'PY'
import json, sys
data = {
    "category": "", "changelog": "Initial release: configured TVBox JSON sources with direct media URLs",
    "description": "Browse explicitly configured direct TVBox JSON providers",
    "guid": "dc586128-c5ae-4ac4-bfb0-931c92ed14cb", "name": "DongmanYaa",
    "overview": "Configured catalogue sources for Jellyfin", "owner": "kail85",
    "targetAbi": "10.11.0.0", "version": "0.1.0.0", "status": "Active",
    "autoUpdate": False, "assemblies": []
}
with open(sys.argv[1], "w", encoding="utf-8") as output:
    json.dump(data, output, ensure_ascii=False, indent=2)
    output.write("\n")
PY
python3 - "$ROOT/artifacts/Jellyfin.Plugin.DongmanYaa_0.1.0.0.zip" "$STAGE" <<'PY'
import pathlib, sys, zipfile
archive, source = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
    for path in source.iterdir():
        output.write(path, f"{source.name}/{path.name}")
print(archive)
PY
