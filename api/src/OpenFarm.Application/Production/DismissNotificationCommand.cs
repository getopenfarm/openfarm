using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

public sealed record DismissNotificationCommand(Guid PrintId);

/// <summary>Leaves a print's undeducted filament as is; the operator can still correct it with manual spool usage.</summary>
public sealed class DismissNotificationCommandHandler(IPrintRepository prints, IProductionEventPublisher publisher)
    : ICommandHandler<DismissNotificationCommand, Guid>
{
    public async Task<Guid> HandleAsync(DismissNotificationCommand command, CancellationToken cancellationToken)
    {
        var print = await prints.GetByIdAsync(command.PrintId, cancellationToken)
            ?? throw new PrintJobNotFoundException(command.PrintId);
        print.DismissFilamentFollowUp();
        await prints.SaveChangesAsync(cancellationToken);
        await publisher.NotificationsChangedAsync(cancellationToken);
        return print.Id;
    }
}
