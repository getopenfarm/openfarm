using System.IO.Compression;
using System.Text.Json;
using OpenFarm.Domain.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>Decodes alert deltas. Null is absent/invalid; empty explicitly clears a source.</summary>
public static class BambuAlertParser
{
    private static readonly Lazy<JsonDocument> Messages = new(() => Load("hms_en.json.gz"));
    private static readonly Lazy<JsonDocument> Links = new(() => Load("wiki_links.json.gz"));

    public static (IReadOnlyList<PrinterAlert>? Hms, IReadOnlyList<PrinterAlert>? PrintErrors) Parse(string payload, string modelCode)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("print", out var print) || print.ValueKind != JsonValueKind.Object)
                return (null, null);
            List<PrinterAlert>? hms = null;
            if (print.TryGetProperty("hms", out var entries) && entries.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<PrinterAlert>();
                var valid = true;
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("attr", out var attrValue)
                        || !entry.TryGetProperty("code", out var codeValue) || !TryUInt(attrValue, out var attr) || !TryUInt(codeValue, out var code))
                    { valid = false; break; }
                    if (attr == 0 || code == 0) continue;
                    var key = $"{attr:X8}{code:X8}";
                    var formatted = $"{attr >> 16:X4}_{attr & 0xffff:X4}_{code >> 16:X4}_{code & 0xffff:X4}";
                    var severity = (code >> 16) switch { 1 => "Error", 2 => "Warning", 3 or 4 => "Information", _ => "Unknown" };
                    var link = Lookup(Links.Value.RootElement, key, modelCode);
                    parsed.Add(new PrinterAlert("Hms", formatted, severity, Message("device_hms", key, modelCode),
                        link is null ? null : $"https://wiki.bambulab.com{link}"));
                }
                if (valid) hms = parsed;
            }
            List<PrinterAlert>? errors = null;
            if (print.TryGetProperty("print_error", out var errorValue) && TryUInt(errorValue, out var error))
            {
                errors = [];
                if (error != 0)
                {
                    var cancelled = error == 50348044;
                    errors.Add(new PrinterAlert("PrintError", $"{error >> 16:X4}_{error & 0xffff:X4}",
                        cancelled ? "Information" : "Error", cancelled ? "Print cancelled by user." : Message("device_error", $"{error:X8}", modelCode),
                        cancelled ? null : "https://wiki.bambulab.com/en/hms/error-code"));
                }
            }
            return (hms, errors);
        }
        catch (JsonException) { return (null, null); }
    }

    private static bool TryUInt(JsonElement value, out uint number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out number);
    }

    private static string Message(string family, string key, string model) =>
        Messages.Value.RootElement.TryGetProperty(family, out var data) ? Lookup(data, key, model) ?? "Unknown printer alert." : "Unknown printer alert.";

    private static string? Lookup(JsonElement data, string key, string model)
    {
        if (!data.TryGetProperty(key, out var variants)) return null;
        string? fallback = null;
        foreach (var variant in variants.EnumerateObject())
        {
            if (variant.Value.GetArrayLength() == 0) fallback = variant.Name;
            if (variant.Value.EnumerateArray().Any(m => string.Equals(m.GetString(), model, StringComparison.OrdinalIgnoreCase))) return variant.Name;
        }
        return fallback;
    }

    private static JsonDocument Load(string name)
    {
        using var resource = typeof(BambuAlertParser).Assembly.GetManifestResourceStream($"OpenFarm.BambuAlerts.{name}")
            ?? throw new InvalidOperationException($"Missing Bambu alert resource: {name}");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
}
