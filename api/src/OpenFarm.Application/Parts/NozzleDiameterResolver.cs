using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Printers;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Parts;

/// <summary>
/// The farm's auto-detected nozzle diameter (FR-005a) — the mode among every registered printer
/// whose <see cref="Printer.MachineProfileId"/> is set and whose linked profile has a parseable
/// nozzle size, tie-broken by the smallest diameter (a conservative, broadly compatible default).
/// Small, deterministic, well-specified — TDD (research.md §5/§12).
/// </summary>
public static class NozzleDiameterResolver
{
    public static decimal? Resolve(IReadOnlyList<Printer> printers, IReadOnlyList<MachineProfile> profiles, IProfileBundleParser profileParser)
    {
        var profilesById = profiles.ToDictionary(p => p.Id);

        var candidates = new List<decimal>();
        foreach (var printer in printers)
        {
            if (printer.MachineProfileId is not { } profileId || !profilesById.TryGetValue(profileId, out var profile))
            {
                continue;
            }

            var displayFields = profileParser.ExtractMachineDisplayFields(profile.RawDataJson, profile.SourceFormat);
            if (displayFields.NozzleSizeMm is { } nozzleSizeMm)
            {
                candidates.Add((decimal)nozzleSizeMm);
            }
        }

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
