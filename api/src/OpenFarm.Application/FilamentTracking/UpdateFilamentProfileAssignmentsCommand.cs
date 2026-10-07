using OpenFarm.Application.Common;
using OpenFarm.Application.Printers;
using OpenFarm.Application.Production;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record UpdateFilamentProfileAssignmentsCommand(Guid FilamentId, IReadOnlyList<FilamentProfileAssignmentInput> Assignments);

public sealed class UpdateFilamentProfileAssignmentsCommandHandler(IFilamentRepository filaments,
    FilamentProfileAssignmentService assignments, IPrinterRepository printers, IProductionDispatcher dispatcher)
    : ICommandHandler<UpdateFilamentProfileAssignmentsCommand, Filament>
{
    public async Task<Filament> HandleAsync(UpdateFilamentProfileAssignmentsCommand command, CancellationToken cancellationToken)
    {
        var filament = await filaments.GetByIdAsync(command.FilamentId, cancellationToken)
            ?? throw new FilamentNotFoundException(command.FilamentId);
        await assignments.ReplaceAsync(filament, command.Assignments, cancellationToken);
        await filaments.SaveChangesAsync(cancellationToken);
        foreach (var printer in await printers.ListAsync(cancellationToken)) dispatcher.RequestDispatchAttempt(printer.Id);
        return filament;
    }
}
