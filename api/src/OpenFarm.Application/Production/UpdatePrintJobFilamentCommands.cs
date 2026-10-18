using OpenFarm.Application.Common;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record SetPrintJobFilamentRequirementsCommand(Guid JobId, IReadOnlyList<JobFilamentRequirementSpec> Requirements);

public sealed class SetPrintJobFilamentRequirementsCommandHandler(IPrintJobRepository repository, ProductionDispatchRequestService dispatchRequests,
    OpenFarm.Application.Parts.PartFilamentSelectionValidator? filamentValidator = null,
    OpenFarm.Application.Profiles.IFilamentProfileRepository? profiles = null)
    : ICommandHandler<SetPrintJobFilamentRequirementsCommand, PrintJob>
{
    public async Task<PrintJob> HandleAsync(SetPrintJobFilamentRequirementsCommand command, CancellationToken cancellationToken)
    {
        var job = await repository.GetByIdAsync(command.JobId, cancellationToken) ?? throw new PrintJobNotFoundException(command.JobId);
        if (filamentValidator is not null)
            foreach (var selection in command.Requirements)
                await filamentValidator.ValidateAsync(selection.MaterialType, selection.NarrowFilamentVariantId, cancellationToken);
        if (profiles is not null)
            foreach (var selection in command.Requirements)
                if (selection.NarrowFilamentProfileId is { } id && await profiles.GetByIdAsync(id, cancellationToken) is null)
                    throw new OpenFarm.Application.Printers.InvalidProfileReferenceException("filament", id);
        job.SetFilamentRequirements(command.Requirements);
        repository.TrackNewFilamentConsumption(job.Filaments);
        await repository.SaveChangesAsync(cancellationToken);
        await dispatchRequests.RequestIdlePrintersAsync(cancellationToken);
        return job;
    }
}
