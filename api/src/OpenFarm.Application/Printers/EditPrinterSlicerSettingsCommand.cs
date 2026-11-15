using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

/// <summary>
/// Sets a printer's slicer settings (today: the build plate). Applied exactly as sent: <c>null</c> clears the
/// plate, so the caller submits the full current settings, not a diff. Never blocked by the printer's status: a
/// running print never re-reads the plate, and queued files made for another plate are skipped on their next
/// eligibility check.
/// </summary>
/// <param name="ActorId">
/// The requesting operator's id (or <c>null</c> for a system call), used to attribute the granular
/// <c>PrinterBuildPlateSelected</c>/<c>Cleared</c> entry the handler writes.
/// </param>
public sealed record EditPrinterSlicerSettingsCommand(
    Guid PrinterId,
    BuildPlateType? BuildPlateType,
    Guid? ActorId,
    ActorType ActorType);
