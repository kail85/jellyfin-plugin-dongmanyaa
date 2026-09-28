#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APK="$ROOT/reverse-engineering/dongmanyaa_10.1.0.apk"
EXPECTED="ccd266958681553217795d7f8633911c6f5b960a8f7f9041b2725422560e7c62"
ACTUAL="$(sha256sum "$APK" | cut -d' ' -f1)"
[[ "$ACTUAL" == "$EXPECTED" ]]
rg -q 'com\.github\.tvbox\.osc' "$ROOT/reverse-engineering/architecture.md"
rg -q 'home, category, detail, search and player' "$ROOT/reverse-engineering/architecture.md"
rg -q 'signed app bootstrap' "$ROOT/reverse-engineering/architecture.md"
echo "APK hash and reverse-engineering notes verified."
