#!/bin/sh
set -eu

DATA_DIR="/app/data"
mkdir -p "$DATA_DIR"

if [ "$(id -u)" = "0" ]; then
  uid="${APP_UID:-1654}"
  chown -R "$uid:$uid" "$DATA_DIR"
  if ! command -v setpriv >/dev/null 2>&1; then
    echo "setpriv is required to drop root before starting the app" >&2
    exit 1
  fi
  exec setpriv --reuid="$uid" --regid="$uid" --clear-groups -- dotnet JevMcp.App.dll "$@"
fi

exec dotnet JevMcp.App.dll "$@"
