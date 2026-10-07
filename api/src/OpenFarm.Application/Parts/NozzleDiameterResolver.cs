using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Parts;

/// <summary>Most common confirmed telemetry nozzle, with ties resolved to the smallest diameter.</summary>
public static class NozzleDiameterResolver
{
    public static decimal? Resolve(IReadOnlyList<Printer> printers)
    {
        var candidates = printers.Where(p => p.NozzleConfirmed && p.ReportedNozzleDiameterMm is > 0)
            .Select(p => p.ReportedNozzleDiameterMm!.Value).ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First()
            .Key;
    }
}
