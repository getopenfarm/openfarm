using System.Globalization;
using System.Text.Json.Nodes;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Snapmaker;

internal sealed class U1ExceptionTracker
{
    private JsonArray _active = [];
    private readonly List<JsonObject> _notifications = [];
    private string? _state;

    internal void SetActive(JsonNode? value)
    {
        if (value is not JsonArray array) return;
        _active = (JsonArray)array.DeepClone();
        var codes = _active.OfType<JsonObject>().Select(Key).ToHashSet();
        _notifications.RemoveAll(n => codes.Contains(Key(n)));
    }

    internal void Notify(JsonNode? value)
    {
        if (value is not JsonObject obj || Key(obj) is not { } key) return;
        _notifications.RemoveAll(n => Key(n) == key);
        _notifications.Insert(0, (JsonObject)obj.DeepClone());
        if (_notifications.Count > 10) _notifications.RemoveAt(10);
    }

    internal void Observe(string? state)
    {
        if (state != _state && state is "printing" or "standby") _notifications.Clear();
        _state = state;
    }

    internal IReadOnlyList<PrinterAlert> Alerts => _active.OfType<JsonObject>().Concat(_notifications)
        .Where(n => Key(n) is not null).DistinctBy(Key).Select(n =>
        {
            var id = Number(n["id"]) ?? 0;
            var module = id switch { 522 => "Motion", 523 => "Toolhead", 524 => "Camera", 525 => "Filament feed", 526 => "Heated bed", 527 => "Chamber", 528 => "Homing", 529 => "G-code", 530 => "Probe/calibration", 531 => "Print file", 532 => "Defect detection", 533 => "Purifier", 2052 => "System", _ => $"Module {id}" };
            return new PrinterAlert(MoonrakerStatusMapper.AlertSource, Key(n)!, (Number(n["level"]) ?? 3) >= 2 ? "Error" : "Warning", $"{module}: {n["message"]?.ToString() ?? "error"}");
        }).ToArray();

    private static double? Number(JsonNode? n) => double.TryParse(n?.ToString(), CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;
    private static string? Key(JsonObject n) => Number(n["id"]) is { } id && Number(n["index"]) is { } index && Number(n["code"]) is { } code
        ? $"U1-{Math.Max(0, Math.Truncate(id)):0000}-{Math.Max(0, Math.Truncate(index)):0000}-{Math.Max(0, Math.Truncate(code)):0000}" : null;
}
