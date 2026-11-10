using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <param name="Expected">When set, the job must still be in this phase (the one the caller read and confirmed).</param>
public sealed record CancelPrintJobCommand(Guid JobId, PrintJobPhase? Expected = null);
