# OpenFarm

OpenFarm is a print-farm management web application. The backend uses ASP.NET Core
and PostgreSQL; the frontend uses React, Vite, and Bun.

## Local development prerequisites

- .NET 10 SDK (the backend pins its version in [api/global.json](api/global.json)).
- Bun for the frontend.
- PostgreSQL, either installed locally or running through Docker Compose.
- Bash for `dev.sh`.

Run the commands below from the repository root. See [CLAUDE.md](CLAUDE.md)
for backend/frontend commands and architecture, and
[docs/TECH_STACKS.md](docs/TECH_STACKS.md) for technology decisions.

## Set up PostgreSQL

Choose one of the following options. The examples use the local development
credentials in [.env.example](.env.example): database and user `openfarm`, password
`changeme`. Substitute your own password where appropriate.

### Option A: Install locally on Ubuntu/Debian

Install the distribution's PostgreSQL package and start its service:

```bash
sudo apt-get update
sudo apt-get install -y postgresql postgresql-client
sudo systemctl enable --now postgresql
```

For a new installation, create the application login and its database:

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 <<'SQL'
CREATE ROLE openfarm WITH LOGIN PASSWORD 'changeme';
CREATE DATABASE openfarm OWNER openfarm;
SQL
```

Run the creation snippet once. If the role or database already exists, keep the
existing data and skip its corresponding `CREATE` statement. To set the password
on an existing `openfarm` role, use:

```bash
sudo -u postgres psql -v ON_ERROR_STOP=1 <<'SQL'
ALTER ROLE openfarm WITH LOGIN PASSWORD 'changeme';
SQL
```

An existing database must grant the configured login permission to apply
migrations. If your database already uses another owner/login, use that login
and its password in the connection string below.

### Option B: Run PostgreSQL with Docker

Install Docker with the Compose plugin, then start only the PostgreSQL service
from the existing [Compose configuration](infra/docker-compose.yml). This uses
PostgreSQL 18 and lets the API and frontend run locally through `dev.sh`.

```bash
# Preserve existing configuration; copy the example only if this file is absent.
if [ ! -e infra/.env ]; then
  cp .env.example infra/.env
fi
# Adjust POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB, and POSTGRES_PORT if needed.
docker compose --env-file infra/.env -f infra/docker-compose.yml up -d postgres --wait
```

### Configure the native API connection

For either option, export the connection string in every shell where you run the
backend or ingestion. Match the username, password, database, and published port
to the PostgreSQL instance you chose:

```bash
export ConnectionStrings__Default='Host=localhost;Port=5432;Database=openfarm;Username=openfarm;Password=changeme'
```

`dev.sh` does **not** load `.env` or `infra/.env`. Those files configure Docker
Compose; copying one does not supply credentials to a native `dotnet` process.
The exported variable overrides the development settings, whose default
connection string uses `Username=postgres` without a password.

Check the same connection using TCP and password authentication (requires the
PostgreSQL client; enter the configured password when prompted):

```bash
psql -h localhost -p 5432 -U openfarm -d openfarm -W -c 'SELECT current_user, current_database();'
```

## Run development and profile ingestion

Restore dependencies, provision slicer bundles, and ingest their system profiles:

```bash
dotnet restore api/OpenFarm.slnx
(cd web && bun install)
./dev.sh slicers
./dev.sh ingest
```

Both API startup and `./dev.sh ingest` automatically apply database migrations
and seed initial data after connecting. There is no separate migration command
needed for this setup. Ingestion can be rerun against the provisioned bundles.

Start the API and frontend:

```bash
./dev.sh up
```

The frontend runs at `http://localhost:5173`; the API runs at
`http://localhost:8888` and `https://localhost:8889`. You can also use
`./dev.sh api` and `./dev.sh web` in separate terminals. Run the backend through
`dev.sh` so it finds the slicer bundles in the repository's `.slicers` directory.

The default seeded login is `admin` / `admin-openfarm`, unless overridden by
`Seed__AdminUsername` and `Seed__AdminPassword` before the first startup.

For the complete Docker stack instead of native development:

```bash
docker compose --env-file infra/.env -f infra/docker-compose.yml up --build
```

## PostgreSQL troubleshooting

- **`No password has been provided ... SASL/SCRAM-SHA-256`:** PostgreSQL requires
  a password, but the API did not receive one. Export `ConnectionStrings__Default`
  as shown above in the same terminal before rerunning `./dev.sh ingest` or
  `./dev.sh api`. This failure happens before migrations; it does not indicate
  a missing schema migration.
- **`password authentication failed`:** Match the connection string to the
  actual role and password. For a native installation, the `ALTER ROLE` snippet
  above sets the password for `openfarm`.
- **Port 5432 is occupied:** A native PostgreSQL service and Docker PostgreSQL
  cannot both bind the same host port. Choose one, or set `POSTGRES_PORT=5433`
  in `infra/.env` for Docker and use port `5433` in both the API connection string
  and the `psql` check.
- **Changing Docker credentials has no effect:** `POSTGRES_USER`,
  `POSTGRES_PASSWORD`, and `POSTGRES_DB` initialize a new data volume only.
  An existing volume retains its original roles, passwords, and databases. Use
  its existing credentials, or update the role through `psql` with an authorized
  database login. Keep the volume to preserve its data.
