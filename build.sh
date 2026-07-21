#!/usr/bin/env bash
# Runs the NUKE build (build/Build.cs) — the same entry point used locally and in CI
# (.github/workflows/ci.yml) and by infra/docker/api.Dockerfile for slicer provisioning.
# Usage: ./build.sh <Target> [<Target>...] [--<parameter> <value>]
set -euo pipefail
cd "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec dotnet run --project build/_build.csproj --configuration Release -- "$@"
