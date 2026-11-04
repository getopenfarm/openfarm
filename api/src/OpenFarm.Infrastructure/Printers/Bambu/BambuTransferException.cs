namespace OpenFarm.Infrastructure.Printers.Bambu;

internal enum BambuTransferStage { Connecting, CheckingSize, Uploading, Verifying }

/// <summary>The transfer component has already exhausted retries; dispatch must not multiply them.</summary>
internal sealed class BambuTransferException(BambuTransferStage stage, string message, Exception? inner = null)
    : IOException(message, inner)
{
    internal BambuTransferStage Stage { get; } = stage;
}

/// <summary>Publication may have reached the printer. Only subsequent telemetry can settle the start.</summary>
internal sealed class BambuStartUnconfirmedException(Exception inner)
    : PrinterStartUnconfirmedException(inner);
