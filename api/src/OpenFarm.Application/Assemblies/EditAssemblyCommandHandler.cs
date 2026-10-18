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
        if (command.Lines is null)
        {
            var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, cancellationToken)
                ?? throw new AssemblyNotFoundException(command.AssemblyId);

            assembly.Rename(command.Name, command.Description);
            await assemblyRepository.SaveChangesAsync(cancellationToken);

            return assembly;
        }

        foreach (var line in command.Lines.Where(l => l.LineId is null))
        {
            await BomLineTargets.EnsureExistsAsync(line.TargetKind, line.TargetId, partRepository, assemblyRepository, nonPrintedComponentRepository, cancellationToken);
        }

        var edits = command.Lines
            .Select(l => new BomLineEdit(l.LineId, BomLineTargets.ToSpec(l.TargetKind, l.TargetId, l.Quantity)))
            .ToList();

        return await assemblyRepository.RunWithStructuralLockAsync(
            async ct =>
            {
                var assembly = await assemblyRepository.GetByIdAsync(command.AssemblyId, ct)
                    ?? throw new AssemblyNotFoundException(command.AssemblyId);

                assembly.Rename(command.Name, command.Description);
                var added = assembly.ReplaceLines(edits);

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
