#!/usr/bin/env bash

set -euo pipefail
umask 077

servicehub_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$servicehub_root"
mkdir -p backups
servicehub_name="servicehub-backup-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"

docker compose --env-file .env.pilot -f compose.pilot.yaml exec -T servicehub \
  /app/backup-server.sh "/data/$servicehub_name"
servicehub_container=$(docker compose --env-file .env.pilot -f compose.pilot.yaml ps -q servicehub)
docker cp "$servicehub_container:/data/$servicehub_name" "backups/$servicehub_name"
docker compose --env-file .env.pilot -f compose.pilot.yaml exec -T servicehub rm -f -- "/data/$servicehub_name"
chmod 600 "backups/$servicehub_name"
printf 'Verified pilot backup copied to %s\n' "backups/$servicehub_name"
