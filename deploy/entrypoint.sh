#!/bin/sh
set -e

PUID=${PUID:-1000}
PGID=${PGID:-1000}

# Own the data dir (part files, camera snapshots, DataProtection keys), then drop root.
mkdir -p /data/part-files /data/camera-snapshots /data/keys
chown -R "${PUID}:${PGID}" /data

# Load the System profile catalog from the bundled slicers: one catalog per slicer version, refreshed in place on
# every start (unchanged profiles write nothing; a newly bundled version becomes its own catalog). Only for the
# default `dotnet OpenFarm.Api.dll` command, so one-off `compose run` commands stay fast.
if [ "$1" = "dotnet" ]; then
  setpriv --reuid="${PUID}" --regid="${PGID}" --clear-groups dotnet OpenFarm.Api.dll ingest-system-profiles \
    || echo "WARNING: System profile ingestion failed; profile pickers stay empty until it is re-run." >&2
fi

exec setpriv --reuid="${PUID}" --regid="${PGID}" --clear-groups "$@"
