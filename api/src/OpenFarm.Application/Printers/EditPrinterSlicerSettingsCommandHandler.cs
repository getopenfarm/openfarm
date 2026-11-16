using OpenFarm.Application.ActivityLog;
using OpenFarm.Application.Common;
using OpenFarm.Application.Production;
using OpenFarm.Domain.ActivityLog;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Application.Printers;

public sealed class EditPrinterSlicerSettingsCommandHandler(
    IPrinterRepository printerRepository,
    IActivityLogWriter activityLogWriter,
    IProductionDispatcher? dispatcher = null) : ICommandHandler<EditPrinterSlicerSettingsCommand, Printer>
{
    public async Task<Printer> HandleAsync(EditPrinterSlicerSettingsCommand command, CancellationToken cancellationToken)
    {
        var printer = await printerRepository.GetByIdAsync(command.PrinterId, cancellationToken)
            ?? throw new PrinterNotFoundException(command.PrinterId);

        // Gated purely by the printer's own registered Brand/ModelCode (PrinterModelCatalog). Resolve cannot fail
        // here: they passed catalog resolution at registration and are immutable thereafter.
        if (command.BuildPlateType is { } requested)
        {
            var model = PrinterModelCatalog.Resolve(printer.Brand, printer.ModelCode);
            if (model.CompatiblePlateTypes is null || !model.CompatiblePlateTypes.Contains(requested))
            {
                throw new IncompatibleBuildPlateException(requested, printer.Brand, printer.ModelCode);
            }
        }

        var previous = printer.BuildPlateType;
        if (command.BuildPlateType is { } plate)
        {
            printer.SelectBuildPlate(plate);
        }
        else
        {
            printer.ClearBuildPlate();
        }


        // Capture the semantic action with the plate change.
        // Only when the plate changed: the caller resends the full settings on every save.
        if (printer.BuildPlateType != previous)
        {
            var eventType = printer.BuildPlateType is not null ? "PrinterBuildPlateSelected" : "PrinterBuildPlateCleared";
            await activityLogWriter.StageAsync(
                ActivityLogEntry.Create("Printer", printer.Id.ToString(), eventType, command.ActorId, command.ActorType),
                cancellationToken);
        }

        await printerRepository.SaveChangesAsync(cancellationToken);

        // Fixing "Needs setup" frees an Idle printer for the queue.
        if (printer.Status == PrinterStatus.Idle) dispatcher?.RequestDispatchAttempt(printer.Id);
        return printer;
    }
}
