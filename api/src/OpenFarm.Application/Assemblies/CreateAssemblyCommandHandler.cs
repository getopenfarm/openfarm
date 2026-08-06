using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// Resolves each line's target existence, then — only when the new assembly has at least one
/// Assembly-kind line — acquires the per-organization structural lock (research.md R3) and runs
/// <see cref="AssemblyGraph.ValidateSave"/> for every such line against the organization's edge list
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
        await EnsureTargetsExistAsync(command.Lines, cancellationToken);

        var lineSpecs = command.Lines.Select(ToSpec).ToList();
        var hasAssemblyLine = command.Lines.Any(l => l.TargetKind == BomLineTargetKind.Assembly);

        if (!hasAssemblyLine)
        {
            var assembly = Assembly.Create(Organization.WellKnownId, command.Name, command.Description, lineSpecs);
            await assemblyRepository.AddAsync(assembly, cancellationToken);
            return assembly;
        }

        return await assemblyRepository.RunWithStructuralLockAsync(
            Organization.WellKnownId,
            async ct =>
            {
                var assembly = Assembly.Create(Organization.WellKnownId, command.Name, command.Description, lineSpecs);

                var edges = await graphQueryService.GetOrganizationEdgesAsync(Organization.WellKnownId, ct);
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

    private async Task EnsureTargetsExistAsync(IReadOnlyList<AssemblyLineInput> lines, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            switch (line.TargetKind)
            {
                case BomLineTargetKind.Part:
                    if (await partRepository.GetByIdAsync(line.TargetId, cancellationToken) is null)
                    {
                        throw new PartNotFoundException(line.TargetId);
                    }

                    break;
                case BomLineTargetKind.Assembly:
                    if (!await assemblyRepository.ExistsAsync(line.TargetId, cancellationToken))
                    {
                        throw new AssemblyNotFoundException(line.TargetId);
                    }

                    break;
                case BomLineTargetKind.NonPrintedComponent:
                    if (!await nonPrintedComponentRepository.ExistsAsync(line.TargetId, cancellationToken))
                    {
                        throw new NonPrintedComponentNotFoundException(line.TargetId);
                    }

                    break;
            }
        }
    }

    private static BomLineSpec ToSpec(AssemblyLineInput line) => line.TargetKind switch
    {
        BomLineTargetKind.Part => new BomLineSpec(BomLineTargetKind.Part, line.TargetId, null, null, line.Quantity),
        BomLineTargetKind.Assembly => new BomLineSpec(BomLineTargetKind.Assembly, null, line.TargetId, null, line.Quantity),
        BomLineTargetKind.NonPrintedComponent => new BomLineSpec(BomLineTargetKind.NonPrintedComponent, null, null, line.TargetId, line.Quantity),
        _ => throw new ArgumentOutOfRangeException(nameof(line)),
    };
}
