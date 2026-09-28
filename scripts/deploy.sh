#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PLUGIN_ROOT="${JELLYFIN_PLUGIN_ROOT:-/Volume1/@apps/jellyfin/data/plugins}"
BACKUP_ROOT="${JELLYFIN_BACKUP_ROOT:-/Volume1/@apps/jellyfin/data/plugin-backups}"
LOG_DIR="${JELLYFIN_LOG_DIR:-/Volume1/@apps/jellyfin/log}"
BASE_URL="${JELLYFIN_URL:-http://192.168.1.50:8098}"
PACKAGE="$ROOT/artifacts/Jellyfin.Plugin.DongmanYaa_0.1.0.0"
TARGET="$PLUGIN_ROOT/Jellyfin.Plugin.DongmanYaa_0.1.0.0"
if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root to back up this plugin, install it and restart jellyfin.service." >&2
  exit 2
fi

"$ROOT/scripts/package.sh"
mkdir -p "$BACKUP_ROOT" "$PLUGIN_ROOT"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
BACKUP="$BACKUP_ROOT/Jellyfin.Plugin.DongmanYaa_$STAMP"
mkdir -p "$BACKUP"
python3 - "$PLUGIN_ROOT" "$BACKUP" <<'PY'
import pathlib, shutil, sys
root, backup = map(pathlib.Path, sys.argv[1:])
for item in root.glob("Jellyfin.Plugin.DongmanYaa_*"):
    if item.is_dir(): shutil.copytree(item, backup / item.name)
PY
LOG_FILE="$(find "$LOG_DIR" -maxdepth 1 -type f -name 'log_*.log' -printf '%T@ %p\n' | sort -nr | head -n1 | cut -d' ' -f2-)"
LOG_LINES=0
if [[ -n "$LOG_FILE" && -f "$LOG_FILE" ]]; then LOG_LINES="$(wc -l < "$LOG_FILE")"; fi
VERSION_BEFORE="$(curl --fail --silent --show-error --max-time 5 "$BASE_URL/System/Info/Public" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Version"])')"
restore_previous() {
  echo "Restoring previous DongmanYaa plugin state from $BACKUP" >&2
  systemctl stop jellyfin.service || true
  python3 - "$PLUGIN_ROOT" "$BACKUP" <<'PY'
import pathlib, shutil, sys
root, backup = map(pathlib.Path, sys.argv[1:])
for item in root.glob("Jellyfin.Plugin.DongmanYaa_*"):
    if item.is_dir(): shutil.rmtree(item)
for item in backup.glob("Jellyfin.Plugin.DongmanYaa_*"):
    if item.is_dir(): shutil.copytree(item, root / item.name)
PY
  systemctl start jellyfin.service
  JELLYFIN_URL="$BASE_URL" "$ROOT/scripts/healthcheck.sh"
}
if ! systemctl stop jellyfin.service; then
  echo "Could not stop Jellyfin; restoring its active state." >&2
  systemctl start jellyfin.service || true
  exit 1
fi
if ! python3 - "$PLUGIN_ROOT" "$PACKAGE" <<'PY'
import pathlib, shutil, sys
root, package = map(pathlib.Path, sys.argv[1:])
for item in root.glob("Jellyfin.Plugin.DongmanYaa_*"):
    if item.is_dir(): shutil.rmtree(item)
shutil.copytree(package, root / package.name)
PY
then
  echo "Plugin file installation failed; rolling back." >&2
  restore_previous
  exit 1
fi
if ! chown -R jellyfin:jellyfin "$TARGET"; then
  echo "Could not set plugin ownership; rolling back." >&2
  restore_previous
  exit 1
fi
if ! systemctl start jellyfin.service || ! JELLYFIN_URL="$BASE_URL" "$ROOT/scripts/healthcheck.sh"; then
  restore_previous
  exit 1
fi
LATEST_LOG="$(find "$LOG_DIR" -maxdepth 1 -type f -name 'log_*.log' -printf '%T@ %p\n' | sort -nr | head -n1 | cut -d' ' -f2-)"
if [[ -z "$LATEST_LOG" || ! -f "$LATEST_LOG" ]]; then
  echo "Jellyfin log unavailable after restart; rolling back." >&2
  restore_previous
  exit 1
fi
if [[ "$LATEST_LOG" != "$LOG_FILE" ]]; then LOG_LINES=0; fi
NEW_LOG="$(tail -n +$((LOG_LINES + 1)) "$LATEST_LOG")"
if ! grep -Fq 'Loaded plugin: "DongmanYaa" "0.1.0.0"' <<<"$NEW_LOG" || grep -Eiq 'DongmanYaa.*(error|exception|failed)|(error|exception|failed).*DongmanYaa' <<<"$NEW_LOG"; then
  echo "Plugin load/log verification failed; rolling back." >&2
  restore_previous
  exit 1
fi
VERSION_AFTER="$(curl --fail --silent --show-error --max-time 5 "$BASE_URL/System/Info/Public" | python3 -c 'import json,sys; print(json.load(sys.stdin)["Version"])')"
if [[ "$VERSION_AFTER" != "$VERSION_BEFORE" ]]; then
  echo "Jellyfin version changed from $VERSION_BEFORE to $VERSION_AFTER; rolling back." >&2
  restore_previous
  exit 1
fi
echo "Installed DongmanYaa 0.1.0.0 on Jellyfin $VERSION_AFTER. Backup: $BACKUP"
