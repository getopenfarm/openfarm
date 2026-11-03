---
title: Upgrades and backups
description: Preserve database, files, and keys together when upgrading or restoring OpenFarm.
---

Back up PostgreSQL and the application data volume as one recovery set. A database alone does not contain the uploaded files or keys needed to use stored printer credentials.

## Back up an installation

For the [supplied production Compose file](docker.md), run from its directory. Pause new work and stop the application so files and database records stay consistent while the backup runs. Let active prints finish first when possible.

```bash
backup_dir="backups/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$backup_dir"
docker compose stop openfarm
docker compose exec -T postgres sh -c 'pg_dump -U "$POSTGRES_USER" "${POSTGRES_DB:-$POSTGRES_USER}"' > "$backup_dir/openfarm.sql"
```

Confirm the dump succeeded before continuing. Copy the stopped application's `/data` out of its container, which remains available after stopping:

```bash
docker compose cp openfarm:/data "$backup_dir/application-data"
docker compose start openfarm
```

Store the SQL dump, copied data (including `keys/`), Compose file, `.env`, and any encryption certificate securely together. If any backup step fails, retain previous backups and restart the application when appropriate. Test recovery on an isolated installation before depending on a backup.

## Upgrade

After taking a backup:

```bash
docker compose pull
docker compose stop openfarm
docker compose up -d openfarm
docker compose logs --tail=100 openfarm
```

Migrations run on startup. Keep the existing project directory/name so Compose reuses the same named volumes. Use a specific release image tag if you want controlled upgrades instead of following `latest`.

Only one application instance may use the database. Expect brief downtime; do not perform a rolling deployment with overlapping instances.

## Restore

1. Prepare an isolated installation with the application version corresponding to the backup. Keep the application stopped and start PostgreSQL only.
2. Restore the dump into an empty database using its configured owner. Restore the **contents** of the saved application data to the volume mounted at `/data`.
3. Restore matching configuration and certificates, check file ownership for `PUID`/`PGID`, and verify that the original encryption keys are present.
4. Start a single application instance and inspect logs. Confirm sign-in, files, printer credentials, and queued work before returning the farm to use.

Rollback requires a compatible application **and** database schema. Starting an old image against a migrated database is not a restore strategy. Do not use `docker compose down -v` on an installation whose data you need to keep.
