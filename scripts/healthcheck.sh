#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${JELLYFIN_URL:-http://192.168.1.50:8098}"
for attempt in $(seq 1 45); do
  if curl --fail --silent --show-error --max-time 3 "$BASE_URL/health" >/dev/null 2>&1; then
    echo "Jellyfin healthy at $BASE_URL"
    exit 0
  fi
  sleep 2
done
echo "Jellyfin did not become healthy at $BASE_URL" >&2
exit 1
