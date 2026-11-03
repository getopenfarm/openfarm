---
title: Docker installation
description: Run a single OpenFarm instance with PostgreSQL on a Linux host.
---

Use the repository's production Compose files to run OpenFarm and PostgreSQL on a Linux host. The supplied configuration uses host networking so OpenFarm can discover printers on the LAN.

## Requirements

- Docker Engine and the Compose plugin.
- A host that can reach your printers' LAN addresses.
- Persistent storage for the database and application data.
- Port **6868** available for the web application and **5432** for PostgreSQL with the supplied host-network layout.

The released image is built for **linux/amd64**. Do not assume an ARM image is available.

## Prepare the files

Create a directory for the installation. Download these two repository files into it:

- [deploy/docker-compose.prod.yml](https://github.com/getopenfarm/openfarm/blob/main/deploy/docker-compose.prod.yml), saved as `docker-compose.yml`.
- [deploy/.env.example](https://github.com/getopenfarm/openfarm/blob/main/deploy/.env.example), saved as `.env`.

If you already have the repository checked out:

```bash
mkdir -p openfarm-install
cp deploy/docker-compose.prod.yml openfarm-install/docker-compose.yml
cp deploy/.env.example openfarm-install/.env
cd openfarm-install
```

Edit `.env` **before the first start**:

```dotenv
POSTGRES_USER=openfarm
POSTGRES_PASSWORD=replace-with-a-strong-database-password
POSTGRES_DB=openfarm
Database__Host=127.0.0.1
Seed__AdminUsername=admin
Seed__AdminPassword=replace-with-a-strong-admin-password
```

Make sure the configured password matches the database password. PostgreSQL uses initialization variables only when it creates a new volume.

The current source Compose file puts OpenFarm on the host network but leaves PostgreSQL on a private container network. Add this **inside its existing `postgres` service** so `Database__Host=127.0.0.1` can reach it:

```yaml
    ports:
      - "127.0.0.1:5432:5432"
```

Keep the loopback address so the database port is not published to the LAN. If PostgreSQL already runs on the host, configure that database instead of starting a second server on the same port.

## Start the farm

```bash
docker compose up -d
docker compose logs --tail=100 openfarm
```

On startup, OpenFarm applies database migrations, creates the first administrator account, and imports the bundled slicer profiles. Open `http://<server-ip>:6868` and sign in with the configured administrator credentials.

If you omitted `Seed__AdminPassword`, the initial password is **admin-openfarm**. Change it before allowing other people to reach the installation. Seed settings do not reset an existing account's password.

## Storage and network layout

The supplied Compose file uses named volumes `data` and `postgres`. Application data is mounted at `/data` and includes part files and cryptographic keys. Keep both volumes when recreating containers.

Host networking supports Bambu SSDP and Snapmaker mDNS discovery. The host's firewall controls which network connections can reach OpenFarm. Restrict database access to the OpenFarm host. If multicast discovery is unavailable with your container network setup, add printers by IP address.

:::caution One application instance
Run exactly one OpenFarm application instance per installation. Do not scale the service or run overlapping application containers against the same database during an upgrade.
:::

Continue with [Configuration](configuration.md), then the [Quick start](../quick-start.md).
