using System.Globalization;

namespace OpenFarm.Infrastructure.Production;

/// <summary>
/// Reads how much filament each tool extrudes in each layer of a sliced G-code file (Bambu Studio / OrcaSlicer dialect).
/// Layers come from <c>; layer num/total_layer_count: N/T</c> — the same N the printer reports as <c>layer_num</c> —
/// or, for other profiles, <c>;LAYER_CHANGE</c>. Extrusion is the net E travel (so a retract and its un-retract cancel)
/// of linear (<c>G0/G1</c>) and arc (<c>G2/G3</c>) moves, attributed to the tool selected by <c>T&lt;n&gt;</c>; filament flushed at a colour change therefore counts to the new colour.
/// Anything before the first layer (start-up purge) is ignored.
/// </summary>
internal static class GCodeFilamentCurveReader
{
    private const string BambuLayerMarker = "; layer num/total_layer_count:";

    /// <summary>Per tool: cumulative net filament (mm) at the end of each layer, index 0 being layer 1. Tools that never extruded are omitted.</summary>
    public static IReadOnlyDictionary<int, double[]> Read(TextReader reader)
    {
        var perLayer = new Dictionary<int, List<double>>();
        var layer = 0;
        var tool = 0;
        var relative = false;
        var lastE = 0.0;
        var bambuMarkers = false;

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.AsSpan().Trim();
            if (line.IsEmpty) continue;

            if (line[0] == ';')
            {
                if (line.StartsWith(BambuLayerMarker, StringComparison.Ordinal))
                {
                    var number = line[BambuLayerMarker.Length..].TrimStart();
                    var slash = number.IndexOf('/');
                    if (slash > 0 && int.TryParse(number[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    {
                        layer = n;
                        bambuMarkers = true;
                    }
                }
                else if (!bambuMarkers && line.StartsWith(";LAYER_CHANGE", StringComparison.Ordinal)) layer++;
                continue;
            }

            var comment = line.IndexOf(';');
            var code = comment >= 0 ? line[..comment].TrimEnd() : line;
            if (code.IsEmpty) continue;

            switch (code[0])
            {
                // The first print filament is selected by the AMS switch in the start G-code, with no T<n> of its own.
                case 'M' when layer == 0 && TryGetAmsSwitch(code, out var initial): tool = initial; break;
                case 'M' when IsWord(code, "M83"): relative = true; break;
                case 'M' when IsWord(code, "M82"): relative = false; break;
                case 'T' when int.TryParse(code[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var t):
                    // T255 (unload) and T1000 (machine start) select no AMS filament.
                    if (t < 255) tool = t;
                    break;
                case 'G' when IsWord(code, "G92") && TryGetE(code, out var reset): lastE = reset; break;
                case 'G' when (IsWord(code, "G1") || IsWord(code, "G0") || IsWord(code, "G2") || IsWord(code, "G3")) && TryGetE(code, out var e):
                    var delta = relative ? e : e - lastE;
                    if (!relative) lastE = e;
                    if (layer >= 1) Add(perLayer, tool, layer, delta);
                    break;
            }
        }

        return perLayer
            .Select(kv => (kv.Key, Cumulative: Cumulate(kv.Value, layer)))
            .Where(t => t.Cumulative[^1] > 0)
            .ToDictionary(t => t.Key, t => t.Cumulative);
    }

    private static void Add(Dictionary<int, List<double>> perLayer, int tool, int layer, double delta)
    {
        if (!perLayer.TryGetValue(tool, out var layers)) perLayer[tool] = layers = [];
        while (layers.Count < layer) layers.Add(0);
        layers[layer - 1] += delta;
    }

    /// <summary>Running total per layer, padded to every layer in the file; never decreasing (a net retract can't un-use filament).</summary>
    private static double[] Cumulate(List<double> layers, int layerCount)
    {
        var cumulative = new double[Math.Max(layerCount, layers.Count)];
        var running = 0.0;
        for (var i = 0; i < cumulative.Length; i++)
        {
            running = Math.Max(running, running + (i < layers.Count ? layers[i] : 0));
            cumulative[i] = running;
        }

        return cumulative;
    }

    /// <summary><c>M620 S&lt;n&gt;A</c> — switch to AMS filament n; S255 is unload.</summary>
    private static bool TryGetAmsSwitch(ReadOnlySpan<char> code, out int filament)
    {
        filament = 0;
        const string prefix = "M620 S";
        if (!code.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = code[prefix.Length..];
        var end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end])) end++;
        return end > 0 && end < rest.Length && rest[end] == 'A'
            && int.TryParse(rest[..end], NumberStyles.None, CultureInfo.InvariantCulture, out filament) && filament < 255;
    }

    private static bool IsWord(ReadOnlySpan<char> code, string word) =>
        code.StartsWith(word, StringComparison.Ordinal) && (code.Length == word.Length || char.IsWhiteSpace(code[word.Length]));

    private static bool TryGetE(ReadOnlySpan<char> code, out double e)
    {
        e = 0;
        for (var i = 1; i < code.Length; i++)
        {
            if (code[i] != 'E' || !char.IsWhiteSpace(code[i - 1])) continue;
            var end = i + 1;
            while (end < code.Length && !char.IsWhiteSpace(code[end])) end++;
            return double.TryParse(code[(i + 1)..end], NumberStyles.Float, CultureInfo.InvariantCulture, out e);
        }

        return false;
    }
}
