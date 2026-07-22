namespace OpenFarm.Application.Printers;

// FR-007 — a process profile is only ever meaningful paired to a specific machine; the printer must
// already have (or be simultaneously getting) a machine profile selected.
public sealed class ProcessProfileRequiresMachineProfileException()
    : Exception("Select a machine profile before selecting a process profile.");
