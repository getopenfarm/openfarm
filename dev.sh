#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

API=api/src/OpenFarm.Api/OpenFarm.Api.csproj
API_DIR=api/src/OpenFarm.Api

# `dotnet run --project` sets CWD to the project dir, so the config's relative './.slicers'
# misses the repo-root .slicers that tools/provision-slicers fills. Anchor it here.
export SlicerBundle__RootDirectory="$PWD/.slicers"

case "$(uname -s)" in
  Darwin) PLATFORM=macos ;;
  Linux)  PLATFORM=linux ;;
  *)      PLATFORM=windows ;;
esac

# The API resolves relative storage paths against its project dir (`dotnet run`'s CWD).
api_path() { case "$1" in /*) echo "$1" ;; *) echo "$API_DIR/${1#./}" ;; esac; }

# Drops the whole public schema (not just tables) plus every local state dir, so the next
# `setup`/`api`/`ingest` run migrates and seeds as if on a fresh machine.
reset_all() {
  local conn="${ConnectionStrings__Default:-Host=localhost;Port=5432;Database=openfarm;Username=openfarm;Password=changeme}"
  local kv key val parts
  IFS=';' read -ra parts <<< "$conn"
  for kv in "${parts[@]}"; do
    [ -n "$kv" ] || continue
    key="$(printf '%s' "${kv%%=*}" | tr -d ' ' | tr '[:upper:]' '[:lower:]')"
    val="${kv#*=}"
    case "$key" in
      host|server)          export PGHOST="$val" ;;
      port)                 export PGPORT="$val" ;;
      database)             export PGDATABASE="$val" ;;
      username|userid|user) export PGUSER="$val" ;;
      password)             export PGPASSWORD="$val" ;;
    esac
  done

  local dirs=(
    "$(api_path "${DataProtection__KeysDirectory:-./keys}")"
    "$(api_path "${PartFileStorage__RootDirectory:-./data/part-files}")"
    "$(api_path "${CameraSnapshotStorage__RootDirectory:-./data/camera-snapshots}")"
    "$SlicerBundle__RootDirectory"
    web/node_modules
  )

  echo "Dropping schema 'public' in database '${PGDATABASE:-}' on ${PGHOST:-}:${PGPORT:-5432} and deleting:"
  printf '  %s\n' "${dirs[@]}"

  psql -v ON_ERROR_STOP=1 -q -c \
    'SET client_min_messages = warning; DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public; GRANT ALL ON SCHEMA public TO CURRENT_USER;'

  local d
  for d in "${dirs[@]}"; do
    [ -n "$d" ] && [ "$d" != / ] && rm -rf "$d"
  done
  echo "Done — run ./dev.sh setup to reinstall, provision slicers, and re-migrate/seed."
}

case "${1:-help}" in
#: api      backend on :6868/:8889 (auto-migrates + seeds)
api)     dotnet run --project="$API" --launch-profile=https --urls="https://0.0.0.0:8889;http://0.0.0.0:6868" ;;
#: web      vite dev server on :5173
web)     cd web && VITE_API_BASE_URL="${VITE_API_BASE_URL:-}" bun run dev --host 0.0.0.0 ;;
#: up       api + web in one terminal, Ctrl-C stops both
# `kill 0` signals the whole process group, which is what reaps `dotnet run`'s grandchildren.
# Run this from an interactive shell only — job control gives it its own process group there.
# Invoked from another script (no job control) it would signal the caller too.
# Web waits for /health/ready so an open tab doesn't flood vite's proxy with ECONNREFUSED.
up)      trap 'kill 0' EXIT INT TERM
         "$0" api &
         api_pid=$!
         echo "Waiting for API health check..."
         until curl -sf -o /dev/null http://localhost:6868/health/ready; do
           kill -0 "$api_pid" 2>/dev/null || { echo "API exited before becoming healthy" >&2; exit 1; }
           sleep 1
         done
         "$0" web ;;
#: slicers  download slicer bundles into ./.slicers for this platform
slicers) dotnet run --project=tools/provision-slicers -- \
           --manifest slicers.manifest.json --out "$SlicerBundle__RootDirectory" --platform "$PLATFORM" ;;
#: ingest   ingest system profiles from the provisioned slicers
ingest)  dotnet run --project="$API" -- ingest-system-profiles ;;
#: setup    bun install + provision slicers + ingest system profiles (migrates + seeds)
setup)   (cd web && bun install)
         "$0" slicers
         "$0" ingest ;;
#: reset    wipe DB schema, local keys/data, .slicers, node_modules — fresh system
reset)   reset_all ;;
#: test     dotnet test + vitest (runs both even if one fails)
test)    rc=0
         dotnet test api/OpenFarm.slnx || rc=1
         (cd web && bun run test) || rc=1
         exit $rc ;;
#: fmt      dotnet format + biome
fmt)     rc=0
         dotnet format api/OpenFarm.slnx || rc=1
         (cd web && bun run format) || rc=1
         exit $rc ;;
*)       sed -n 's/^#: /  dev.sh /p' "$0"; exit 1 ;;
esac
