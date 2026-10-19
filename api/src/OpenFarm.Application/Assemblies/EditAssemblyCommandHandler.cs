using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;

namespace OpenFarm.Application.Assemblies;

/// <summary>
/// Renames/redescribes and, when <see cref="EditAssemblyCommand.Lines"/> is given, fully replaces the
/// BOM (<see cref="Assembly.ReplaceLines"/>) — all under the structural lock with one save, running
/// <see cref="AssemblyGraph.ValidateSave"/> for every newly added Assembly-kind line.
/// </summary>
public sealed class EditAssemblyCommandHandler(
    IAssemblyRepository assemblyRepository,
    INonPrintedComponentRepository nonPrintedComponentRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService) : ICommandHandler<EditAssemblyCommand, Assembly>
{
    public async Task<Assembly> HandleAsync(EditAssemblyCommand command, CancellationToken cancellationToken)
    {
        return await assemblyRepository.RunWithStructuralLockAsync(
            async ct =>
            {
                await AssemblyCosts.ValidateAsync(command.AdditionalCosts, nonPrintedComponentRepository, ct);
                if (command.Lines is not null)
                {
                    foreach (var line in command.Lines.Where(l => l.TargetKind == BomLineTargetKind.NonPrintedComponent))
                    {
                        var item = await nonPrintedComponentRepository.GetByIdAsync(line.TargetId, ct)
                            ?? throw new NonPrintedComponentNotFoundException(line.TargetId);
                        item.ValidateUsage(line.Quantity, bom: true);
                    }
                }

                foreach (var line in (command.Lines ?? []).Where(l => l.LineId is null))
                {
                    await BomLineTargets.EnsureExistsAsync(line.TargetKind, line.TargetId, partRepository, assemblyRepository, nonPrintedComponentRepository, ct);
                }

                var edits = command.Lines?
                    .Select(l => new BomLineEdit(l.LineId, BomLineTargets.ToSpec(l.TargetKind, l.TargetId, l.Quantity)))
                    .ToList();

                var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, ct)
                    ?? throw new AssemblyNotFoundException(command.AssemblyId);

                assembly.Rename(command.Name, command.Description);
                var added = edits is null ? [] : assembly.ReplaceLines(edits);
                if (command.AdditionalCosts is not null)
                {
                    foreach (var costLine in assembly.ReplaceAdditionalCosts(command.AdditionalCosts))
                        assemblyRepository.StageCostLine(costLine);
                }
                assembly.ValidateCostDuplicates();

                if (added.Any(l => l.TargetKind == BomLineTargetKind.Assembly))
                {
                    var graph = AssemblyGraph.FromEdges(await graphQueryService.GetEdgesAsync(ct));
                    foreach (var line in added.Where(l => l.TargetKind == BomLineTargetKind.Assembly))
                    {
                        graph.ValidateSave(assembly.Id, line.TargetAssemblyId!.Value);
                    }
                }

                foreach (var line in added)
                {
                    assemblyRepository.StageLine(line);
                }

                await assemblyRepository.SaveChangesAsync(ct);
                return assembly;
            },
            cancellationToken);
    }
}
