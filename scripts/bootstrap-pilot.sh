#!/usr/bin/env bash

set -euo pipefail
umask 077

servicehub_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
cd "$servicehub_root"

for servicehub_command in docker openssl curl; do
  command -v "$servicehub_command" >/dev/null || { printf 'Missing command: %s\n' "$servicehub_command" >&2; exit 1; }
done
docker compose version >/dev/null

mkdir -p secrets
if [[ ! -f secrets/bootstrap-password.txt ]]; then
  openssl rand -base64 36 > secrets/bootstrap-password.txt
  chmod 600 secrets/bootstrap-password.txt
fi

if [[ ! -f .env.pilot ]]; then
  servicehub_jwt=$(openssl rand -hex 48)
  sed "s/generated-by-bootstrap-script/$servicehub_jwt/" .env.pilot.example > .env.pilot
  chmod 600 .env.pilot
fi

docker compose --env-file .env.pilot -f compose.pilot.yaml config --quiet
docker compose --env-file .env.pilot -f compose.pilot.yaml pull
docker compose --env-file .env.pilot -f compose.pilot.yaml up -d

for _ in {1..90}; do
  if curl -fsS http://127.0.0.1:5000/health/ready >/dev/null \
      && curl -fsS http://127.0.0.1:5001/health >/dev/null; then
    printf '%s\n' 'FlatHack ServiceHub pilot is ready at http://127.0.0.1:5001/.'
    printf '%s\n' 'Read the one-time bootstrap password locally from secrets/bootstrap-password.txt.'
    printf '%s\n' 'After first login: enroll MFA, set SERVICEHUB_BOOTSTRAP_ENABLED=false, and restart Compose.'
    exit 0
  fi
  sleep 2
done

docker compose --env-file .env.pilot -f compose.pilot.yaml logs --tail 100 servicehub >&2
printf '%s\n' 'Pilot did not become ready within three minutes.' >&2
exit 1
