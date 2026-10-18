namespace OpenFarm.Domain.Production;

/// <summary>
/// A print the printer started itself (Bambu Studio, the Handy app, the touchscreen, an SD card): no queue, run or part behind it.
/// It is recorded so its filament is deducted like a dispatched job's. Which spool fed each tool isn't reported, so the trays the
/// printer names as active are recorded as they appear; a lone tool takes the first of those trays, several are matched by material and colour (see <see cref="BindTools"/>).
/// </summary>
public sealed class ExternalPrint : Print
{
    private ExternalPrint(Guid id, Guid? printerId, string name, DateTimeOffset? startedAtUtc)
        : base(id, name, PrintSource.External, PrintJobStatus.Printing, DateTimeOffset.UtcNow)
    {
        PrinterId = printerId;
        StartedAtUtc = startedAtUtc;
    }

    /// <summary>Active tray ids in order of first appearance (<c>unit*4+slot</c>, 254 external); paired with <see cref="ObservedSpoolIds"/> (Guid.Empty = no spool known).</summary>
    public int[] ObservedTrayIds { get; private set; } = [];

    public Guid[] ObservedSpoolIds { get; private set; } = [];

    public static ExternalPrint Start(Guid printerId, string name, IEnumerable<ExternalFilamentSpec> filaments)
    {
        var print = new ExternalPrint(Guid.NewGuid(), printerId, name, DateTimeOffset.UtcNow);
        foreach (var spec in filaments.OrderBy(f => f.ToolIndex))
            print.AddFilament(PrintFilament.Create(print.Id, spec));
        return print;
    }

    /// <summary>The tray is new, or was seen before the spool on it was known, so looking the spool up again can still help.</summary>
    public bool WantsSpoolFor(int trayId)
    {
        if (IsClosed) return false;
        var index = Array.IndexOf(ObservedTrayIds, trayId);
        return index < 0 || ObservedSpoolIds[index] == Guid.Empty;
    }

    /// <summary>
    /// Records an active tray and the spool loaded there. A tray first seen with no known spool takes one when it becomes known
    /// (a spool loaded or a feeding slot chosen mid-print); a known spool is never replaced. False when nothing changed.
    /// </summary>
    public bool ObserveTray(int trayId, Guid? spoolId)
    {
        if (IsClosed) return false;
        var index = Array.IndexOf(ObservedTrayIds, trayId);
        if (index < 0)
        {
            ObservedTrayIds = [.. ObservedTrayIds, trayId];
            ObservedSpoolIds = [.. ObservedSpoolIds, spoolId ?? Guid.Empty];
            return true;
        }

        if (ObservedSpoolIds[index] != Guid.Empty || spoolId is not { } known) return false;
        // A new array, so EF sees the property change.
        var spools = (Guid[])ObservedSpoolIds.Clone();
        spools[index] = known;
        ObservedSpoolIds = spools;
        return true;
    }

    /// <summary>
    /// Binds the spool when the print ends: one tool takes the first tray seen. Several tools can't be paired by the order trays
    /// appeared (a print joined mid-print misses early trays, and tool order is not tray order), so they are bound afterwards by
    /// <see cref="BindTools"/>, from the material and colour each tool prints.
    /// </summary>
    protected override void OnClosed()
    {
        var tools = Filaments.ToList();
        if (tools.Count == 1 && ObservedSpoolIds.Length > 0) tools[0].Bind(Known(0));
    }

    /// <summary>Binds the spool for each tool that has none yet; tools absent from <paramref name="spoolByTool"/> stay unbound.</summary>
    public void BindTools(IReadOnlyDictionary<int, Guid> spoolByTool)
    {
        foreach (var line in Filaments.Where(f => f.SpoolId is null))
            if (spoolByTool.TryGetValue(line.ToolIndex, out var spoolId)) line.Bind(spoolId);
    }

    private Guid? Known(int index) => ObservedSpoolIds[index] == Guid.Empty ? null : ObservedSpoolIds[index];
}
