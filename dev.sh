#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"

API=api/src/OpenFarm.Api/OpenFarm.Api.csproj

# `dotnet run --project` sets CWD to the project dir, so the config's relative './.slicers'
# misses the repo-root .slicers that tools/provision-slicers fills. Anchor it here.
export SlicerBundle__RootDirectory="$PWD/.slicers"

case "$(uname -s)" in
  Darwin) PLATFORM=macos ;;
  Linux)  PLATFORM=linux ;;
  *)      PLATFORM=windows ;;
esac

case "${1:-help}" in
#: api      backend on :8888/:8889 (auto-migrates + seeds)
api)     dotnet run --project="$API" --profile=https ;;
#: web      vite dev server on :5173
web)     cd web && bun run dev ;;
#: up       api + web in one terminal, Ctrl-C stops both
# `kill 0` signals the whole process group, which is what reaps `dotnet run`'s grandchildren.
# Run this from an interactive shell only — job control gives it its own process group there.
# Invoked from another script (no job control) it would signal the caller too.
up)      trap 'kill 0' EXIT INT TERM; "$0" api & "$0" web ;;
#: slicers  download slicer bundles into ./.slicers for this platform
slicers) dotnet run --project=tools/provision-slicers -- \
           --manifest slicers.manifest.json --out "$SlicerBundle__RootDirectory" --platform "$PLATFORM" ;;
#: ingest   ingest system profiles from the provisioned slicers
ingest)  dotnet run --project="$API" -- ingest-system-profiles ;;
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
