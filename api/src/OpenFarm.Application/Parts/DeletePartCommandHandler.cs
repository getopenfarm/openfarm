using Microsoft.Extensions.Logging;
using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed partial class DeletePartCommandHandler(
    IPartRepository partRepository, IPartEventPublisher eventPublisher, ILogger<DeletePartCommandHandler>? logger = null)
    : ICommandHandler<DeletePartCommand, bool>
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Part {PartId} was deleted but its notification could not be published")]
    private static partial void LogNotificationFailure(ILogger logger, Guid partId, Exception exception);

    public async Task<bool> HandleAsync(DeletePartCommand command, CancellationToken cancellationToken)
    {
        var part = await partRepository.GetByIdAsync(command.PartId, cancellationToken)
            ?? throw new PartNotFoundException(command.PartId);

        // The repository evaluates all dependencies under the deletion lock.
        await partRepository.RemoveAsync(part, cancellationToken);
        // Content-addressed files can be shared by other parts, processing jobs and pinned
        // production revisions. Retain bytes until reference-aware garbage collection exists.
        try
        {
            await eventPublisher.PartDeletedAsync(part.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            // The database commit is authoritative; clients also invalidate their own queries.
            if (logger is not null) LogNotificationFailure(logger, part.Id, ex);
        }

        return true;
    }
}
