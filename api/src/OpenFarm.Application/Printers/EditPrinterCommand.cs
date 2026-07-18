namespace OpenFarm.Application.Printers;

/// <summary>
/// Single edit operation covering a printer's name and, optionally, its connection details
/// (research.md §1). <see cref="IpAddress"/>/<see cref="AccessCode"/> must be supplied together or
/// not at all — validated by <see cref="EditPrinterCommandValidator"/>, not enforced here.
/// <see cref="Brand"/>/<see cref="ModelCode"/>/<see cref="SerialNumber"/> are optional and, when
/// supplied, are checked (never applied) against the printer's current value — a caller sending a
/// mismatched value is rejected outright (FR-002/FR-010) rather than having the value silently
/// dropped.
/// </summary>
public sealed record EditPrinterCommand(
    Guid PrinterId,
    string Name,
    string? IpAddress,
    string? AccessCode,
    string? Brand,
    string? ModelCode,
    string? SerialNumber);
