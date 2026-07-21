using System.Runtime.InteropServices;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Parts;

/// <summary>Resolves the running host's <see cref="SlicerPlatform"/> via <see cref="RuntimeInformation"/> (research.md §4/§9).</summary>
public sealed class HostPlatform : IHostPlatformProvider
{
    public SlicerPlatform Current { get; } = Resolve();

    private static SlicerPlatform Resolve()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return SlicerPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return SlicerPlatform.MacOs;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return SlicerPlatform.Linux;
        }

        throw new PlatformNotSupportedException($"OpenFarm does not support the current host platform ({RuntimeInformation.OSDescription}).");
    }
}
