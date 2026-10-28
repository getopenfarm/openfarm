using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record AddRunLinesCommand(Guid ProductionRunId, IReadOnlyList<RunLineInput> Lines);

/// <summary>
/// More demand on an open run: the same requirements and jobs creating a run makes (<see cref="RunLineMaterializer"/>),
/// queued after everything already waiting. A Complete run is Active again until the new plates are done.
/// </summary>
public sealed class AddRunLinesCommandHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService,
    IProductionEventPublisher publisher,
    ProductionDispatchRequestService? dispatchRequests = null, IAssemblyRepository? assemblyRepository = null, IPartFileStorage? fileStorage = null,
    ProductionSnapshotFactory? snapshotFactory = null, IPrinterRepository? printerRepository = null) : ICommandHandler<AddRunLinesCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(AddRunLinesCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByIdAsync(command.ProductionRunId, cancellationToken)
            ?? throw new ProductionRunNotFoundException(command.ProductionRunId);

        var lines = run.AddLines(command.Lines.Select(l => l.ToSpec()).ToList());
        var materializer = new RunLineMaterializer(
            requirementRepository, jobRepository, partRepository, graphQueryService, printerRepository, assemblyRepository, fileStorage, snapshotFactory);
        var explodedByLine = await materializer.ExplodeAsync(lines, cancellationToken);
        runRepository.TrackNewLines(lines);
        await materializer.StageAsync(lines, explodedByLine, cancellationToken);

        await runRepository.SaveChangesAsync(cancellationToken);
        await publisher.ProductionRunStatusChangedAsync(run.Id, run.Status, cancellationToken);
        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
