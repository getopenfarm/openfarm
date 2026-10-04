using OpenFarm.Application.Common;
using OpenFarm.Domain.Production;

namespace OpenFarm.Application.Production;

/// <summary>FR-009b — editable after queueing; affects only plates dispatched after this call, never a currently-printing one (RunLine.SetRevisionMode's own doc comment).</summary>
public sealed class SetRunLineRevisionModeCommandHandler(IProductionRunRepository runRepository)
    : ICommandHandler<SetRunLineRevisionModeCommand, RunLine>
{
    public async Task<RunLine> HandleAsync(SetRunLineRevisionModeCommand command, CancellationToken cancellationToken)
    {
        var run = await runRepository.GetByRunLineIdAsync(command.RunLineId, cancellationToken);
        if (command.ProductionRunId is { } expected && run?.Id != expected) throw new RunLineNotFoundException(command.RunLineId);
        var line = run?.Lines.FirstOrDefault(l => l.Id == command.RunLineId)
            ?? throw new RunLineNotFoundException(command.RunLineId);

        line.SetRevisionMode(command.RevisionMode);
        await runRepository.SaveChangesAsync(cancellationToken);

        return line;
    }
}
