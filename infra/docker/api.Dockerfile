FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
EXPOSE 8888

# Build context is the repo root (infra/docker-compose.yml) so this stage can reach api/, the NUKE
# build (build/) that now drives provisioning, tools/provision-slicers (the library it calls into),
# and the root-level manifest (plan.md "Structure Decision" — provisioning is deliberately outside
# api/'s dependency graph, FR-018).
COPY . .

# Populates the bundled slicer directory from slicers.manifest.json via the NUKE build's
# ProvisionSlicers target (build/Build.cs; research.md §9) — replaces the old single hard-coded
# OrcaSlicer install. Best-effort, not CI-gated: the real download is manual (quickstart.md), same
# carve-out as physical printer hardware; a failed/partial provisioning run still lets the rest of
# the image build (an empty/incomplete bundle then surfaces as the ordinary "no slicer available"
# path at runtime, not a build failure).
ENV SlicerBundle__RootDirectory=/opt/slicers
RUN ./build.sh ProvisionSlicers --out "$SlicerBundle__RootDirectory" --platform Linux \
    || echo "WARNING: Slicer provisioning failed or was incomplete — Part Library in-app slicing may be unavailable for the missing engine/version until this is fixed; unrelated feature work is unaffected."

ENTRYPOINT ["dotnet", "watch", "run", "--project", "api/src/OpenFarm.Api/OpenFarm.Api.csproj", "--urls", "http://0.0.0.0:8888", "--no-launch-profile"]
