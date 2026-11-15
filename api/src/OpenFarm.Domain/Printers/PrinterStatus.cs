namespace OpenFarm.Domain.Printers;

public enum PrinterStatus
{
    Idle,
    Printing,

    /// <summary>
    /// Shown as "Not ready": the printer takes no job until an operator marks it ready. Entered after a print ends
    /// (the plate is not confirmed clear) or by an operator hold on an Idle printer. The name is the original cause and is
    /// kept so stored values, the API and MCP stay stable.
    /// </summary>
    AwaitingClear,
}
