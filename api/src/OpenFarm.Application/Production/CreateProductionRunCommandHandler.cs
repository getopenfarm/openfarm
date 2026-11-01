using OpenFarm.Application.Assemblies;
using OpenFarm.Application.Common;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Printers;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>Creates the run, then its requirements and finite jobs through <see cref="ProductionDemandMaterializer"/>, all in one transaction.</summary>
public sealed class CreateProductionRunCommandHandler(
    IProductionRunRepository runRepository,
    IPartRequirementRepository requirementRepository,
    IPrintJobRepository jobRepository,
    IPartRepository partRepository,
    IAssemblyGraphQueryService graphQueryService, ProductionDispatchRequestService? dispatchRequests = null, IAssemblyRepository? assemblyRepository = null, IPartFileStorage? fileStorage = null, ProductionSnapshotFactory? snapshotFactory = null, IPrinterRepository? printerRepository = null, ProductionPlanner? planner = null, IProductionDemandRepository? demands = null) : ICommandHandler<CreateProductionRunCommand, ProductionRun>
{
    public async Task<ProductionRun> HandleAsync(CreateProductionRunCommand command, CancellationToken cancellationToken)
    {
        await using var guard = demands is null ? null : await demands.LockAsync(command.SubmissionId ?? Guid.NewGuid(), cancellationToken, command.Lines.Any(l => l.TargetKind == RunLineTargetKind.Assembly));
        var requestJson = System.Text.Json.JsonSerializer.Serialize(command);
        if (demands is not null && command.SubmissionId is { } submissionId)
        {
            var previous = await demands.FindSubmissionAsync(submissionId, cancellationToken);
            if (previous is not null)
            {
                if (previous.SubmissionRequestJson != requestJson || previous.ProductionRunId is null)
                    throw new InvalidProductionDemandException("This submission ID was already used for different work.");
                return await runRepository.GetByIdAsync(previous.ProductionRunId.Value, cancellationToken)
                    ?? throw new ProductionRunNotFoundException(previous.ProductionRunId.Value);
            }
        }
        await ProductionPlanValidation.ValidateAsync(command.Lines, planner, demands, cancellationToken);
        var run = ProductionRun.Create(command.Name, command.DueDate, command.Notes, command.Lines.Select(l => l.ToSpec()).ToList());
        if (command.SubmissionId is { } key) run.Lines[0].SetSubmissionId(key, requestJson);
        var materializer = new ProductionDemandMaterializer(
            requirementRepository, jobRepository, partRepository, graphQueryService, printerRepository, assemblyRepository, fileStorage, snapshotFactory, planner);
        var explodedByLine = await materializer.ExplodeAsync(run.Lines, cancellationToken);
        await runRepository.AddAsync(run, cancellationToken);
        await materializer.StageAsync(run.Lines, explodedByLine, cancellationToken);

        if (demands is null) await runRepository.SaveChangesAsync(cancellationToken);
        else await demands.SaveAsync(cancellationToken);
        if (dispatchRequests is not null) await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return run;
    }
}
