using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>
/// research.md R7 — matches each operator-supplied line against the job's own defaulted
/// <c>(MaterialType, DetectedColorHex)</c> set (throwing <see cref="FilamentConsumptionSumMismatchException"/>,
/// 400, on a line that matches none of them), records it on the job, and decrements the matched
/// <see cref="Spool"/> through its own existing, constitution-mandated <c>RecordUsage</c>.
/// </summary>
public static class PrintJobFilamentConsumptionRecorder
{
    public static async Task RecordAsync(
        PrintJob job, IReadOnlyList<FilamentConsumptionLineInput> lines, ISpoolRepository spoolRepository, CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            var matched = job.FilamentConsumption.FirstOrDefault(c => c.MaterialType == line.MaterialType && c.DetectedColorHex == line.DetectedColorHex)
                ?? throw new FilamentConsumptionSumMismatchException(line.MaterialType, line.DetectedColorHex);

            job.RecordFilamentConsumption(matched.Id, line.SpoolId, line.GramsConsumed);

            if (line.SpoolId is { } spoolId)
            {
                var spool = await spoolRepository.GetByIdAsync(spoolId, cancellationToken);
                spool?.RecordUsage(line.GramsConsumed);
            }
        }
    }
}
