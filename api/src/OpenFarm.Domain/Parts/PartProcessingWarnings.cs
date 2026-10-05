namespace OpenFarm.Domain.Parts;

public static class PartProcessingWarnings
{
    public static IReadOnlyList<string> Create(decimal? nozzle, bool materialComplete, string? estimateReason)
    {
        var warnings = new List<string>();
        if (nozzle is null) warnings.Add("Nozzle diameter is unknown. Configure printer profiles and reprocess, or replace the file with complete metadata.");
        if (!materialComplete) warnings.Add("Material metadata is incomplete. Replace sliced files with a file containing complete material metadata before production.");
        if (estimateReason is not null) warnings.Add(estimateReason);
        return warnings;
    }
}
