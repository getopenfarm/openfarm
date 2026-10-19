using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// Single-level read (research.md R7) — this assembly's own direct lines only, each resolved to a
/// display name per its target kind. The frontend expands a sub-assembly line by calling this same
/// query again with that line's <c>targetId</c>.
/// </summary>
public sealed class GetAssemblyQueryHandler(
    IAssemblyRepository assemblyRepository,
    INonPrintedComponentRepository nonPrintedComponentRepository,
    IPartRepository partRepository) : IQueryHandler<GetAssemblyQuery, AssemblyDetail?>
{
    public async Task<AssemblyDetail?> HandleAsync(GetAssemblyQuery query, CancellationToken cancellationToken)
    {
        var assembly = await assemblyRepository.GetByIdAsync(query.AssemblyId, cancellationToken);
        if (assembly is null)
        {
            return null;
        }

        var lines = new List<BomLineDetail>();
        foreach (var line in assembly.Lines)
        {
            var (targetId, targetName) = await ResolveTargetAsync(line, cancellationToken);
            lines.Add(new BomLineDetail(line.Id, line.TargetKind, targetId, targetName, line.Quantity));
        }

        return new AssemblyDetail(assembly.Id, assembly.Name, assembly.Description, lines, assembly.CreatedAtUtc, assembly.UpdatedAtUtc, assembly.AdditionalCosts.Select(l => l.Input).ToList());
    }

    private async Task<(Guid TargetId, string TargetName)> ResolveTargetAsync(BomLine line, CancellationToken cancellationToken)
    {
        switch (line.TargetKind)
        {
            case BomLineTargetKind.Part:
                var part = await partRepository.GetByIdAsync(line.TargetPartId!.Value, cancellationToken);
                return (line.TargetPartId.Value, part?.Name ?? string.Empty);
            case BomLineTargetKind.Assembly:
                var subAssembly = await assemblyRepository.GetByIdAsync(line.TargetAssemblyId!.Value, cancellationToken);
                return (line.TargetAssemblyId.Value, subAssembly?.Name ?? string.Empty);
            case BomLineTargetKind.NonPrintedComponent:
                var component = await nonPrintedComponentRepository.GetByIdAsync(line.TargetNonPrintedComponentId!.Value, cancellationToken);
                return (line.TargetNonPrintedComponentId.Value, component?.Name ?? string.Empty);
            default:
                throw new ArgumentOutOfRangeException(nameof(line));
        }
    }
}
