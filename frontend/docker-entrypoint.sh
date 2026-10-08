#!/bin/sh
# Writes the runtime config the SPA fetches at boot (tenant id, optional API base).
set -eu
CID="${CUSTOMER_ID:-}"
case "$CID" in *[!0-9a-fA-F-]*) echo "CUSTOMER_ID must be a GUID" >&2; exit 1 ;; esac
printf '{ "customerId": "%s", "apiBase": "%s" }\n' "$CID" "${API_BASE:-}" > /usr/share/nginx/html/config.json
exec nginx -g 'daemon off;'
