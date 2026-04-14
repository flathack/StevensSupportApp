#!/bin/sh
set -eu

cleanup() {
  if [ -n "${ADMINWEB_PID:-}" ]; then
    kill "$ADMINWEB_PID" 2>/dev/null || true
  fi
  if [ -n "${SERVER_PID:-}" ]; then
    kill "$SERVER_PID" 2>/dev/null || true
  fi
  wait 2>/dev/null || true
}

trap cleanup INT TERM EXIT

dotnet /app/server/StevensSupportHelper.Server.dll --urls http://0.0.0.0:5000 &
SERVER_PID=$!

dotnet /app/adminweb/StevensSupportHelper.AdminWeb.dll --urls "${AdminWeb__Urls:-http://0.0.0.0:5001}" &
ADMINWEB_PID=$!

while kill -0 "$SERVER_PID" 2>/dev/null && kill -0 "$ADMINWEB_PID" 2>/dev/null; do
  sleep 1
done

wait "$SERVER_PID" || true
wait "$ADMINWEB_PID" || true
exit 1
