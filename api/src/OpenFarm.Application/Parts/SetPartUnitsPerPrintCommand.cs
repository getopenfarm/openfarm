using OpenFarm.Application.Common;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Application.Parts;

public sealed record SetPartUnitsPerPrintCommand(Guid PartId, int UnitsPerPrint);

public sealed class SetPartUnitsPerPrintCommandHandler(IPartRepository repository)
    : ICommandHandler<SetPartUnitsPerPrintCommand, Part>
{
    public async Task<Part> HandleAsync(SetPartUnitsPerPrintCommand command, CancellationToken cancellationToken)
    {
        var part = await repository.GetByIdAsync(command.PartId, cancellationToken) ?? throw new PartNotFoundException(command.PartId);
        part.SetUnitsPerPrint(command.UnitsPerPrint);
        await repository.SaveChangesAsync(cancellationToken);
        return part;
    }
}
