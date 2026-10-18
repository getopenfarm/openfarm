using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// Resolves each line's target existence, then — only when the new assembly has at least one
/// Assembly-kind line — acquires the structural lock (research.md R3) and runs
/// <see cref="AssemblyGraph.ValidateSave"/> for every such line against the edge list
/// before persisting, all inside one transaction.
/// </summary>
public sealed class CreateAssemblyCommandHandler(
    IAssemblyRepository assemblyRepository,
    INonPrintedComponentRepository nonPrintedComponentRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService) : ICommandHandler<CreateAssemblyCommand, Assembly>
{
    public async Task<Assembly> HandleAsync(CreateAssemblyCommand command, CancellationToken cancellationToken)
    {
        foreach (var line in command.Lines)
        {
            await BomLineTargets.EnsureExistsAsync(line.TargetKind, line.TargetId, partRepository, assemblyRepository, nonPrintedComponentRepository, cancellationToken);
        }

        var lineSpecs = command.Lines.Select(l => BomLineTargets.ToSpec(l.TargetKind, l.TargetId, l.Quantity)).ToList();
        var hasAssemblyLine = command.Lines.Any(l => l.TargetKind == BomLineTargetKind.Assembly);

        if (!hasAssemblyLine)
        {
            var assembly = Assembly.Create(command.Name, command.Description, lineSpecs);
            await assemblyRepository.AddAsync(assembly, cancellationToken);
            return assembly;
        }

        return await assemblyRepository.RunWithStructuralLockAsync(
            async ct =>
            {
                var assembly = Assembly.Create(command.Name, command.Description, lineSpecs);

                var edges = await graphQueryService.GetEdgesAsync(ct);
                var graph = AssemblyGraph.FromEdges(edges);
                foreach (var line in assembly.Lines.Where(l => l.TargetKind == BomLineTargetKind.Assembly))
                {
                    graph.ValidateSave(assembly.Id, line.TargetAssemblyId!.Value);
                }

                await assemblyRepository.AddAsync(assembly, ct);
                return assembly;
            },
            cancellationToken);
    }
}
