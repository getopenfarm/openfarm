namespace OpenFarm.Application.Printers;

public sealed record MarkPrinterReadyCommand(Guid PrinterId, bool ConfirmRunCompletion = false, Guid? ActorId = null);
