namespace OpenFarm.Domain.Production;

public sealed class PinnedJobRequiresPrinterException()
    : Exception("A pinned job (or line default) requires a printer id.");
