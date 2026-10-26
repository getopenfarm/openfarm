# Release image: API + built SPA + bundled slicers. Build from the repo root:
#   docker build -f deploy/Dockerfile -t openfarm:local .
# linux/amd64 only — the slicer manifest has x86-64 Linux builds only.

FROM oven/bun:1.3 AS web
WORKDIR /web
COPY web/package.json web/bun.lock ./
RUN --mount=type=cache,target=/root/.bun/install/cache \
    bun install --frozen-lockfile
COPY web/ ./
RUN bun run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY . .
RUN dotnet publish api/src/OpenFarm.Api/OpenFarm.Api.csproj -c Release -o /out
# Best-effort like infra/docker/api.Dockerfile: a failed download leaves the image without a
# slicer (Part Library slicing unavailable) instead of failing the build.
RUN dotnet run --project tools/provision-slicers -- \
        --manifest slicers.manifest.json --out /opt/slicers --platform linux \
    || echo "WARNING: Slicer provisioning failed or was incomplete — in-app slicing may be unavailable."

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The slicer AppImages (extracted) link GTK/WebKitGTK/OpenGL at load time and refuse to start without the host
# libs, even for headless CLI slicing. Verified with every Linux build in slicers.manifest.json: ldd reports
# nothing missing and `AppRun --help` runs. libmspack0 is bundled only by OrcaSlicer 2.4.x.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libopengl0 libglu1-mesa libwebkit2gtk-4.1-0 libsecret-1-0 libsm6 libmspack0 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=api /out ./
COPY --from=api /opt/slicers /opt/slicers
COPY --from=web /web/dist ./wwwroot
COPY deploy/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh && mkdir -p /data
# The database is the compose `postgres` service; the other Database__* defaults are in appsettings.json.
ENV ASPNETCORE_URLS=http://+:6868 \
    Database__Host=postgres \
    SlicerBundle__RootDirectory=/opt/slicers \
    PartFileStorage__RootDirectory=/data/part-files \
    CameraSnapshotStorage__RootDirectory=/data/camera-snapshots \
    DataProtection__KeysDirectory=/data/keys
EXPOSE 6868
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/6868 && printf "GET /health/live HTTP/1.0\r\n\r\n" >&3 && head -1 <&3 | grep -q 200'
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "OpenFarm.Api.dll"]
