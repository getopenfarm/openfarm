# Deploying OpenFarm

```
mkdir openfarm && cd openfarm
# save docker-compose.prod.yml here as docker-compose.yml and fill in its Required values
docker compose up -d      # http://<host>:8888, log in as admin with Seed__AdminPassword
```

- **Settings:** the image holds every default, so the compose file lists only what you must set. To change another
  setting, add it under `environment:` as `Section__Key` from `appsettings.json`, e.g. `Seed__AdminUsername` (default
  `admin`). The database is the `postgres` service; set `ConnectionStrings__Default` to use another server.
- **Upgrade:** `docker compose pull && docker compose up -d` (migrations run at startup). Replace the existing application instance; do not run an overlapping instance against the same database. Expect brief downtime.
- **From the 0.0.1 `.env` layout:** the old compose file and `.env` keep working. To switch, keep the folder name
  (Compose then reuses the `pg_data` volume) and copy `POSTGRES_PASSWORD` into `PGPASSWORD` and `POSTGRES_PASSWORD`,
  `JWT_SIGNING_KEY` into `Jwt__SigningKey`, plus any `TZ`, `PUID` or `PGID` you changed.
- **Slicer profiles:** the image bundles the slicers listed in `slicers.manifest.json`, and the entrypoint loads their System profiles (machine / process / filament) on every start — one catalog per slicer version, so adding a version never overwrites another. To re-run it by hand:
  `docker compose exec openfarm dotnet OpenFarm.Api.dll ingest-system-profiles` (idempotent).
- **Backup:** `./data` (part files, camera snapshots, DataProtection keys) **and** the Postgres volume
  (`docker compose exec postgres pg_dump -U openfarm openfarm > backup.sql`). Losing `./data/keys`
  makes stored printer secrets undecryptable.
- **Single replica only** — run exactly one OpenFarm application instance per installation, with PostgreSQL as a separate service. Migrations, printer connections, production dispatch, and part-processing workers run inside the API process. Do not use `docker compose up --scale openfarm=2`, rolling deployments with overlapping instances, or multiple application hosts sharing the database. This is an operating constraint, not a distributed lock enforced by the application.
- **Printer discovery:** *Add printer* lists the printers on the server's LAN: Bambu over SSDP (UDP 2021/1990) and the
  Snapmaker U1 over mDNS (UDP 5353). A bridged container hears neither. On Linux, use host networking: give `openfarm`
  `network_mode: host` instead of `ports`, publish postgres on `127.0.0.1:5432:5432`, and set
  `ConnectionStrings__Default=Host=localhost;Database=openfarm;Username=openfarm`. Otherwise add printers by IP.
- **Browser connectivity:** the built frontend uses same-origin `/api` and `/hubs` URLs, including file downloads. Access it using the server's hostname/IP; no frontend URL configuration is needed. Development uses Vite's existing API/WebSocket proxies. `VITE_API_BASE_URL` is an optional build-time override for custom frontend builds, not a runtime Compose setting. Empty values use the current origin.
- **Asset caching:** the HTML shell and unversioned public files revalidate (`no-cache`); fingerprinted Vite assets under `/assets/` cache for one year with `immutable`. Client routes support direct navigation; missing assets and unknown API/hub/health endpoints retain real error responses.
- **Build locally:** `docker build -f deploy/Dockerfile -t openfarm:local .` from the repo root.
- **Release:** push a `X.Y.Z` tag → `.github/workflows/docker-publish.yml` pushes
  `ghcr.io/getopenfarm/openfarm` (`X.Y.Z`, `X.Y`, `sha-*`, `latest`).
