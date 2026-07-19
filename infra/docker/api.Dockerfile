FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
EXPOSE 8888

# Headless OrcaSlicer CLI for Part Library in-app slicing (Parts.ISlicerEngine, research.md §4).
# Best-effort install step, not CI-gated — ISlicerEngine is developed/tested against
# FakeSlicerEngine; real-CLI validation is manual (quickstart.md), same carve-out as physical
# printer hardware. Pinned via build arg so the image can be rebuilt against a newer release
# without editing this file. AppImages need FUSE to run directly, which containers typically
# lack, so it's extracted instead (--appimage-extract) and invoked from the extracted tree.
ARG ORCASLICER_VERSION=2.2.0
RUN curl -fsSL -o /tmp/OrcaSlicer.AppImage \
        "https://github.com/SoftFever/OrcaSlicer/releases/download/v${ORCASLICER_VERSION}/OrcaSlicer_Linux_AppImage_Ubuntu2404_V${ORCASLICER_VERSION}.AppImage" \
    && chmod +x /tmp/OrcaSlicer.AppImage \
    && cd /opt \
    && /tmp/OrcaSlicer.AppImage --appimage-extract >/dev/null \
    && mv squashfs-root /opt/orcaslicer \
    && ln -s /opt/orcaslicer/AppRun /usr/local/bin/orca-slicer-cli \
    && rm /tmp/OrcaSlicer.AppImage \
    || echo "WARNING: OrcaSlicer CLI install failed — Part Library in-app slicing will be unavailable until this is fixed; unrelated feature work is unaffected."

ENTRYPOINT ["dotnet", "watch", "run", "--project", "src/OpenFarm.Api/OpenFarm.Api.csproj", "--urls", "http://0.0.0.0:8888", "--no-launch-profile"]
