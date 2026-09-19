#!/bin/bash
set -euo pipefail
for name in CERT_P12_B64 CERT_PWD APPLE_ID APPLE_APP_SPECIFIC_PASSWORD TEAM_ID; do
  if [ -z "${!name:-}" ]; then
    echo "error: release requires $name" >&2
    exit 1
  fi
done
[ "$TEAM_ID" = 44Y2L8A2CV ] || { echo 'error: unexpected release team' >&2; exit 1; }
