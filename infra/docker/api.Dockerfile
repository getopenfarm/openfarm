FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
EXPOSE 6868

# The slicer AppImages (extracted) refuse to start without the host OpenGL runtime.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libopengl0 libglu1-mesa \
    && rm -rf /var/lib/apt/lists/*

# Build context is the repo root (infra/docker-compose.yml) so this stage can reach both api/ and
# the standalone tools/provision-slicers project + the root-level manifest (plan.md "Structure
# Decision" — the provisioning tool is deliberately outside api/'s dependency graph, FR-018).
COPY . .

# Populates the bundled slicer directory from slicers.manifest.json via the dev/build-time
# provisioning tool (research.md §9) — replaces the old single hard-coded OrcaSlicer install.
# Best-effort, not CI-gated: the real download is manual (quickstart.md), same carve-out as
# physical printer hardware; a failed/partial provisioning run still lets the rest of the image
# build (an empty/incomplete bundle then surfaces as the ordinary "no slicer available" path at
# runtime, not a build failure).
ENV SlicerBundle__RootDirectory=/opt/slicers
RUN dotnet run --project tools/provision-slicers -- \
        --manifest slicers.manifest.json \
        --out "$SlicerBundle__RootDirectory" \
        --platform linux \
    || echo "WARNING: Slicer provisioning failed or was incomplete — Part Library in-app slicing may be unavailable for the missing engine/version until this is fixed; unrelated feature work is unaffected."

ENTRYPOINT ["dotnet", "watch", "run", "--project", "api/src/OpenFarm.Api/OpenFarm.Api.csproj", "--urls", "http://0.0.0.0:6868", "--no-launch-profile"]
