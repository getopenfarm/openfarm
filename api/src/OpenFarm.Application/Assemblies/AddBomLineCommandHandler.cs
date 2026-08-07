using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Organizations;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// Resolves target existence and the FR-013 duplicate check (via <see cref="Assembly.AddLine"/>),
/// and — only when <see cref="AddBomLineCommand.TargetKind"/> is <see cref="BomLineTargetKind.Assembly"/> —
/// acquires the structural lock and runs <see cref="AssemblyGraph.ValidateSave"/> before persisting
/// (mirrors <see cref="CreateAssemblyCommandHandler"/>'s single-line-add case).
/// </summary>
public sealed class AddBomLineCommandHandler(
    IAssemblyRepository assemblyRepository,
    INonPrintedComponentRepository nonPrintedComponentRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService) : ICommandHandler<AddBomLineCommand, BomLine>
{
    public async Task<BomLine> HandleAsync(AddBomLineCommand command, CancellationToken cancellationToken)
    {
        await EnsureTargetExistsAsync(command.TargetKind, command.TargetId, cancellationToken);
        var spec = ToSpec(command);

        if (command.TargetKind != BomLineTargetKind.Assembly)
        {
            var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
                ?? throw new AssemblyNotFoundException(command.AssemblyId);

            var addedLine = assembly.AddLine(spec);
            await assemblyRepository.AddLineAsync(addedLine, cancellationToken);
            return addedLine;
        }

        return await assemblyRepository.RunWithStructuralLockAsync(
            Organization.WellKnownId,
            async ct =>
            {
                var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, ct)
                    ?? throw new AssemblyNotFoundException(command.AssemblyId);

                var edges = await graphQueryService.GetOrganizationEdgesAsync(Organization.WellKnownId, ct);
                var graph = AssemblyGraph.FromEdges(edges);
                graph.ValidateSave(assembly.Id, command.TargetId);

                var addedLine = assembly.AddLine(spec);
                await assemblyRepository.AddLineAsync(addedLine, ct);
                return addedLine;
            },
            cancellationToken);
    }

    private async Task EnsureTargetExistsAsync(BomLineTargetKind targetKind, Guid targetId, CancellationToken cancellationToken)
    {
        switch (targetKind)
        {
            case BomLineTargetKind.Part:
                if (await partRepository.GetByIdAsync(targetId, cancellationToken) is null)
                {
                    throw new PartNotFoundException(targetId);
                }

                break;
            case BomLineTargetKind.Assembly:
                if (!await assemblyRepository.ExistsAsync(targetId, cancellationToken))
                {
                    throw new AssemblyNotFoundException(targetId);
                }

                break;
            case BomLineTargetKind.NonPrintedComponent:
                if (!await nonPrintedComponentRepository.ExistsAsync(targetId, cancellationToken))
                {
                    throw new NonPrintedComponentNotFoundException(targetId);
                }

                break;
        }
    }

    private static BomLineSpec ToSpec(AddBomLineCommand command) => command.TargetKind switch
    {
        BomLineTargetKind.Part => new BomLineSpec(BomLineTargetKind.Part, command.TargetId, null, null, command.Quantity),
        BomLineTargetKind.Assembly => new BomLineSpec(BomLineTargetKind.Assembly, null, command.TargetId, null, command.Quantity),
        BomLineTargetKind.NonPrintedComponent => new BomLineSpec(BomLineTargetKind.NonPrintedComponent, null, null, command.TargetId, command.Quantity),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}
