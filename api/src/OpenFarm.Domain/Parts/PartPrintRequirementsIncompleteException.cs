namespace OpenFarm.Domain.Parts;

public sealed class PartPrintRequirementsIncompleteException(Guid partId)
    : Exception($"Part '{partId}' has incomplete print requirements. Reprocess after configuring printer profiles, or replace the file with complete nozzle and material metadata.");
