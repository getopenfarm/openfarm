#!/bin/sh
set -e

PUID=${PUID:-1000}
PGID=${PGID:-1000}

# Own the data dir (part files, camera snapshots, DataProtection keys), then drop root.
mkdir -p /data/part-files /data/camera-snapshots /data/keys
chown -R "${PUID}:${PGID}" /data

exec setpriv --reuid="${PUID}" --regid="${PGID}" --clear-groups "$@"
