using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Api.Endpoints.Assemblies;

/// <summary>Resolves a single BOM line's display name for the line-level endpoint responses (Add/Edit), mirroring <c>GetAssemblyQueryHandler</c>'s per-kind resolution without a full assembly re-read.</summary>
public sealed class BomLineTargetNameResolver(
    IPartRepository partRepository, IAssemblyRepository assemblyRepository, INonPrintedComponentRepository nonPrintedComponentRepository)
{
    public async Task<string> ResolveAsync(BomLine line, CancellationToken cancellationToken)
    {
        switch (line.TargetKind)
        {
            case BomLineTargetKind.Part:
                var part = await partRepository.GetByIdAsync(line.TargetPartId!.Value, cancellationToken);
                return part?.Name ?? string.Empty;
            case BomLineTargetKind.Assembly:
                var assembly = await assemblyRepository.GetByIdAsync(line.TargetAssemblyId!.Value, cancellationToken);
                return assembly?.Name ?? string.Empty;
            case BomLineTargetKind.NonPrintedComponent:
                var component = await nonPrintedComponentRepository.GetByIdAsync(line.TargetNonPrintedComponentId!.Value, cancellationToken);
                return component?.Name ?? string.Empty;
            default:
                return string.Empty;
        }
    }
}
