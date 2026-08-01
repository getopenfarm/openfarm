using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Single edit operation covering a printer's name and, optionally, its connection details
/// (research.md §1). <see cref="IpAddress"/>/<see cref="AccessCode"/> must be supplied together or
/// not at all — validated by <see cref="EditPrinterCommandValidator"/>, not enforced here.
/// <see cref="Brand"/>/<see cref="ModelCode"/>/<see cref="SerialNumber"/> are optional and, when
/// supplied, are checked (never applied) against the printer's current value — a caller sending a
/// mismatched value is rejected outright (FR-002/FR-010) rather than having the value silently
/// dropped. <see cref="MachineProfileId"/>/<see cref="ProcessProfileId"/> (009, US2) and
/// <see cref="BuildPlateType"/> (012) are applied exactly as sent — <c>null</c> clears the
/// link/selection — unlike the paired connection fields above; the caller is expected to submit the
/// printer's full current state, not a sparse diff (contracts/profiles-api.md,
/// contracts/printers-api.md). <see cref="BuildPlateType"/> has no machine-profile prerequisite,
/// unlike <see cref="ProcessProfileId"/> — gated only by the printer's own registered Brand/ModelCode
/// (FR-002, clarification Q4).
/// </summary>
/// <param name="ActorId">
/// The requesting operator's id (or <c>null</c> for a system-initiated call), threaded through so the
/// handler can attribute the granular <c>PrinterMachineProfileLinked</c>/<c>Unlinked</c>/
/// <c>PrinterProcessProfileLinked</c>/<c>Unlinked</c>/<c>PrinterBuildPlateSelected</c>/<c>Cleared</c>
/// activity-log entries it writes directly (FR-016) — distinct from the base <c>PrinterEdited</c>
/// event the endpoint's <c>IActivityLoggable</c>/global post-processor already writes for every
/// successful call to this same endpoint.
/// </param>
public sealed record EditPrinterCommand(
    Guid PrinterId,
    string Name,
    string? IpAddress,
    string? AccessCode,
    string? Brand,
    string? ModelCode,
    string? SerialNumber,
    Guid? MachineProfileId,
    Guid? ProcessProfileId,
    BuildPlateType? BuildPlateType,
    Guid? ActorId,
    ActorType ActorType);
