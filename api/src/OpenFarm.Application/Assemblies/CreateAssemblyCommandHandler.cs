using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Assemblies;

/// <summary>Validates structure and cost usage, then atomically saves under the shared structural lock.</summary>
public sealed class CreateAssemblyCommandHandler(
    IAssemblyRepository assemblyRepository,
    INonPrintedComponentRepository nonPrintedComponentRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService) : ICommandHandler<CreateAssemblyCommand, Assembly>
{
    public async Task<Assembly> HandleAsync(CreateAssemblyCommand command, CancellationToken cancellationToken)
    {
        return await assemblyRepository.RunWithStructuralLockAsync(
            async ct =>
            {
                foreach (var line in command.Lines)
                {
                    await BomLineTargets.EnsureExistsAsync(line.TargetKind, line.TargetId, partRepository, assemblyRepository, nonPrintedComponentRepository, ct);
                }

                await AssemblyCosts.ValidateAsync(command.AdditionalCosts, nonPrintedComponentRepository, ct);
                foreach (var line in command.Lines.Where(l => l.TargetKind == BomLineTargetKind.NonPrintedComponent))
                {
                    var item = await nonPrintedComponentRepository.GetByIdAsync(line.TargetId, ct)
                        ?? throw new NonPrintedComponentNotFoundException(line.TargetId);
                    item.ValidateUsage(line.Quantity, bom: true);
                }

                var lineSpecs = command.Lines.Select(l => BomLineTargets.ToSpec(l.TargetKind, l.TargetId, l.Quantity)).ToList();
                var assembly = Assembly.Create(command.Name, command.Description, lineSpecs);
                assembly.ReplaceAdditionalCosts(command.AdditionalCosts ?? []);
                assembly.SetPricing(command.Pricing);
                assembly.MoveToFolder(command.FolderId);

                if (assembly.Lines.Any(l => l.TargetKind == BomLineTargetKind.Assembly))
                {
                    var graph = AssemblyGraph.FromEdges(await graphQueryService.GetEdgesAsync(ct));
                    foreach (var line in assembly.Lines.Where(l => l.TargetKind == BomLineTargetKind.Assembly))
                        graph.ValidateSave(assembly.Id, line.TargetAssemblyId!.Value);
                }

                await assemblyRepository.AddAsync(assembly, ct);
                return assembly;
            },
            cancellationToken);
    }
}
