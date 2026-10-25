# Deploying OpenFarm

```
mkdir openfarm && cd openfarm
# save docker-compose.prod.yml here as docker-compose.yml, and .env.example as .env
docker compose up -d      # http://<host>:8888
docker compose logs openfarm | grep "generated password"   # the first login: user admin
```

- **Settings:** the image holds a default for every setting, and `.env` is the only file you edit: change the database
  login there, or add any other setting as `KEY=value`. A `.env` line replaces the default. Common ones:
  `Seed__AdminUsername`, `Seed__AdminEmail`, `Seed__AdminPassword` (first start only), `Jwt__SigningKey`,
  `Database__Host`, `Database__Port`, `TZ` (log timestamps), `PUID` and `PGID` (host user and group that own `./data`).
  Any `Section__Key` from `appsettings.json` works.
- **Generated secrets:** without `Seed__AdminPassword`, the first start creates `admin` with a random password and
  logs it once (there is no reset; keep it). Without `Jwt__SigningKey`, the first start writes a random key to
  `./data/keys/jwt-signing-key`; deleting it only logs everyone out.
- **Database:** `POSTGRES_USER` and `POSTGRES_PASSWORD` in `.env` (shipped as `openfarm` / `openfarm`; change the
  password before the first start) apply to both `postgres` and `openfarm`, and the database is named after the user, as
  in the Postgres image. Postgres reads them only when it creates a new volume, and no port is published. Another
  server: set the `Database__*` keys; a full `ConnectionStrings__Default` also works and wins.
- **Upgrade:** `docker compose pull && docker compose up -d` (migrations run at startup). Replace the existing application instance; do not run an overlapping instance against the same database. Expect brief downtime.
- **From the 0.0.1 `.env` layout:** the old compose file and `.env` keep working. To switch, keep the folder name
  (Compose then reuses the `pg_data` volume) and your `.env`, and add `POSTGRES_USER=openfarm` to it. Rename
  `JWT_SIGNING_KEY` to `Jwt__SigningKey` to keep everyone logged in. `POSTGRES_PASSWORD`, `TZ`, `PUID` and `PGID` keep
  their names; the `ADMIN_*` lines are unused (the admin account is already in the database).
- **Slicer profiles:** the image bundles the slicers listed in `slicers.manifest.json`, and the entrypoint loads their System profiles (machine / process / filament) on every start — one catalog per slicer version, so adding a version never overwrites another. To re-run it by hand:
  `docker compose exec openfarm dotnet OpenFarm.Api.dll ingest-system-profiles` (idempotent).
- **Backup:** `./data` (part files, camera snapshots, DataProtection keys) **and** the Postgres volume
  (`docker compose exec postgres sh -c 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_USER"' > backup.sql`). Losing `./data/keys`
  makes stored printer secrets undecryptable.
- **Single replica only** — run exactly one OpenFarm application instance per installation, with PostgreSQL as a separate service. Migrations, printer connections, production dispatch, and part-processing workers run inside the API process. Do not use `docker compose up --scale openfarm=2`, rolling deployments with overlapping instances, or multiple application hosts sharing the database. This is an operating constraint, not a distributed lock enforced by the application.
- **Printer discovery:** *Add printer* lists the printers on the server's LAN: Bambu over SSDP (UDP 2021/1990) and the
  Snapmaker U1 over mDNS (UDP 5353). A bridged container hears neither. On Linux, use host networking: give `openfarm`
  `network_mode: host` instead of `ports`, publish postgres on `127.0.0.1:5432:5432`, and add
  `Database__Host=localhost` to `.env`. Otherwise add printers by IP.
- **Browser connectivity:** the built frontend uses same-origin `/api` and `/hubs` URLs, including file downloads. Access it using the server's hostname/IP; no frontend URL configuration is needed. Development uses Vite's existing API/WebSocket proxies. `VITE_API_BASE_URL` is an optional build-time override for custom frontend builds, not a runtime Compose setting. Empty values use the current origin.
- **Asset caching:** the HTML shell and unversioned public files revalidate (`no-cache`); fingerprinted Vite assets under `/assets/` cache for one year with `immutable`. Client routes support direct navigation; missing assets and unknown API/hub/health endpoints retain real error responses.
- **Build locally:** `docker build -f deploy/Dockerfile -t openfarm:local .` from the repo root.
- **Release:** push a `X.Y.Z` tag → `.github/workflows/docker-publish.yml` pushes
  `ghcr.io/getopenfarm/openfarm` (`X.Y.Z`, `X.Y`, `sha-*`, `latest`).
