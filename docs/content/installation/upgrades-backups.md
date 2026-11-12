---
title: Upgrades and backups
description: Preserve database, files, and keys together when upgrading or restoring OpenFarm.
---

Back up PostgreSQL and the application data volume as one recovery set. A database alone does not contain the uploaded files or keys needed to use stored printer credentials.

## Back up an installation

For the [supplied production Compose file](docker.md), run these commands from its directory. Let active prints finish first when possible. Pause new work before you stop the application. Stopping OpenFarm keeps files and database records consistent during the backup.

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

Store the SQL dump, copied data (including `keys/`), Compose file, `.env`, and encryption certificates together in a secure location. If a backup step fails, keep previous backups. Restart the application when appropriate. Test recovery on a separate, isolated installation before you rely on the backup.

## Upgrade

The MCP settings update adds installation settings to the database. Token-only MCP installations must enable **Access tokens only** under **Settings → AI assistants** after upgrading. Existing REST tokens remain valid. An explicit legacy `OAUTH_ENABLED=true` with a valid `ORIGIN` preserves sign-in on the first upgrade. See [AI assistant configuration](configuration.md#ai-assistant-sign-in).

After taking a backup:

```bash
docker compose pull
docker compose stop openfarm
docker compose up -d openfarm
docker compose logs --tail=100 openfarm
```

Migrations run on startup. Keep the existing project directory and project name so Compose reuses the same named volumes. To control when you upgrade, use a specific release image tag instead of `latest`.

Only one application instance can use the database. Expect brief downtime. Do not run overlapping instances during an upgrade.

## Restore

1. Prepare an isolated installation with the application version that matches the backup.
2. Keep OpenFarm stopped and start PostgreSQL only.
3. Restore the dump into an empty database using its configured owner.
4. Restore the **contents** of the saved application data to the volume mounted at `/data`.
5. Restore the matching configuration and certificates.
6. Check file ownership against `PUID` and `PGID`.
7. Verify that the original encryption keys are present.
8. Start one OpenFarm application instance.
9. Inspect the logs.
10. Confirm sign-in, files, printer credentials, and queued work before returning the farm to use.

Rollback requires a compatible application **and** database schema. Starting an old image against a migrated database is not a restore strategy. Do not use `docker compose down -v` on an installation whose data you need to keep.
