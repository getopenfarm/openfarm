using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Application.FilamentTracking;

public sealed record LoadSpoolOntoFeedSlotCommand(Guid SpoolId, Guid FeedSlotId);

public sealed record LoadSpoolResult(Spool Spool, bool RfidMismatchWarning, string? PortSharingNote);
