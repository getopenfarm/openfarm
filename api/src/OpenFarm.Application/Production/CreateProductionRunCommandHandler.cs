using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Creates the run, then its requirements and finite jobs through <see cref="RunLineMaterializer"/>, all in one transaction.</summary>
public sealed class CreateProductionRunCommandHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService, ProductionDispatchRequestService? dispatchRequests = null, IAssemblyRepository? assemblyRepository = null, IPartFileStorage? fileStorage = null, ProductionSnapshotFactory? snapshotFactory = null, IPrinterRepository? printerRepository = null) : ICommandHandler<CreateProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CreateProductionRunCommand command, CancellationToken cancellationToken)
    {
        var run = ProductionRun.Create(command.Name, command.DueDate, command.Notes, command.Lines.Select(l => l.ToSpec()).ToList());
        var materializer = new RunLineMaterializer(
            requirementRepository, jobRepository, partRepository, graphQueryService, printerRepository, assemblyRepository, fileStorage, snapshotFactory);
        var explodedByLine = await materializer.ExplodeAsync(run.Lines, cancellationToken);
        await runRepository.AddAsync(run, cancellationToken);
        await materializer.StageAsync(run.Lines, explodedByLine, cancellationToken);

        await runRepository.SaveChangesAsync(cancellationToken);
        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
