using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-001a — <see cref="ProductionRun.Name"/> if set, else derived from contents (e.g. "Bracket ×20" or "3 lines — bracket, knob, base"). Read-side only, never stored.</summary>
public sealed class ProductionRunDisplayLabelResolver(IPartRepository partRepository, IAssemblyRepository assemblyRepository)
{
    public async Task<string> ResolveAsync(ProductionRun run, CancellationToken cancellationToken)
    {
        if (run.Name is not null)
        {
            return run.Name;
        }

        if (run.Lines.Count == 0)
        {
            return "Empty run";
        }

        if (run.Lines.Count == 1)
        {
            var line = run.Lines[0];
            var targetName = await ResolveTargetNameAsync(line, cancellationToken);
            return line.Quantity is null ? targetName : $"{targetName} ×{line.Quantity}";
        }

        var names = new List<string>();
        foreach (var line in run.Lines)
        {
            names.Add(await ResolveTargetNameAsync(line, cancellationToken));
        }

        return $"{run.Lines.Count} lines — {string.Join(", ", names)}";
    }

    private async Task<string> ResolveTargetNameAsync(RunLine line, CancellationToken cancellationToken)
    {
        if (line.TargetKind == RunLineTargetKind.Part)
        {
            var part = await partRepository.GetByIdAsync(line.TargetId, cancellationToken);
            return part?.Name ?? "Unknown part";
        }

        var assembly = await assemblyRepository.GetByIdAsync(line.TargetId, cancellationToken);
        return assembly?.Name ?? "Unknown assembly";
    }
}
