using System.Text.Json;

namespace OpenFarm.Domain.Production;

/// <summary>A point in the print where the layer or the progress percent changed, as the G-code walks through it.</summary>
public sealed record ProgressMark(int Layer, int Percent);

/// <summary>
/// One tool's extrusion read from G-code, in mm: the purge before layer 1, the cumulative total at the end of each layer,
/// and the cumulative total at each <see cref="ProgressMark"/> (parallel to the marks; empty when the file has none).
/// </summary>
public sealed record ToolExtrusion(double PrimeMm, double[] CumulativeMm, double[] MarkMm);

/// <summary>
/// How a job line's filament is spread over the print: the cumulative share (0..1) of its total used by the end of
/// each layer, read from the dispatched G-code. Lets a failed or cancelled multi-colour print deduct what each colour
/// really used by the point it stopped at, instead of assuming every colour is used evenly. The purge before layer 1
/// counts as used from the moment the print starts.
/// </summary>
public sealed class FilamentUsageCurve
{
    private readonly double[] _cumulativeShare;
    private readonly ProgressMark[] _marks;
    private readonly double[] _markShares;

    private FilamentUsageCurve(double[] cumulativeShare, double primeShare, ProgressMark[] marks, double[] markShares)
    {
        _cumulativeShare = cumulativeShare;
        PrimeShare = primeShare;
        _marks = marks;
        _markShares = markShares;
    }

    public int LayerCount => _cumulativeShare.Length;

    /// <summary>Share used by the purge before layer 1.</summary>
    public double PrimeShare { get; }

    /// <summary>
    /// Builds the curve for one line from the cumulative filament per layer (any unit) of each tool it draws from,
    /// summed. Null when those tools extruded nothing, so the caller falls back to an even spread.
    /// </summary>
    public static FilamentUsageCurve? Create(IEnumerable<double[]> cumulativeByTool) =>
        FromExtrusion([], cumulativeByTool.Select(c => new ToolExtrusion(0, c, [])));

    /// <summary>As <see cref="Create"/>, from the tools' full extrusion: purge and per-mark totals included.</summary>
    public static FilamentUsageCurve? FromExtrusion(IReadOnlyList<ProgressMark> marks, IEnumerable<ToolExtrusion> tools)
    {
        var list = tools.Where(t => t.CumulativeMm.Length > 0).ToList();
        if (list.Count == 0) return null;
        var layers = list.Max(t => t.CumulativeMm.Length);
        var prime = list.Sum(t => t.PrimeMm);
        var summed = new double[layers];
        foreach (var tool in list)
            for (var i = 0; i < layers; i++)
                summed[i] += tool.CumulativeMm[Math.Min(i, tool.CumulativeMm.Length - 1)];
        var total = prime + summed[^1];
        if (summed[^1] <= 0 || total <= 0) return null;

        double Share(double mm) => Math.Round(Math.Clamp((prime + mm) / total, 0, 1), 6);
        var markShares = new double[marks.Count];
        var running = 0.0;
        for (var i = 0; i < markShares.Length; i++)
        {
            running = Math.Max(running, Share(list.Sum(t => i < t.MarkMm.Length ? t.MarkMm[i] : 0)));
            markShares[i] = running;
        }

        return new FilamentUsageCurve(summed.Select(Share).ToArray(), Share(0), [.. marks], markShares);
    }

    /// <summary>
    /// Share used when stopped while printing <paramref name="currentLayer"/> (1-based, as the printer reports it) at
    /// <paramref name="percent"/> (as the printer reports it). With a percent the file knows, the stop landed in that
    /// layer-and-percent stretch of the print, so take its midpoint. Otherwise everything through the previous layer
    /// plus half of the current one.
    /// </summary>
    public double ShareAt(int currentLayer, int? percent = null)
    {
        if (currentLayer <= 0) return PrimeShare;
        if (currentLayer > LayerCount) return 1;
        if (percent is { } p && Array.IndexOf(_marks, new ProgressMark(currentLayer, p)) is var i and >= 0)
        {
            var end = i + 1 < _markShares.Length ? _markShares[i + 1] : 1;
            return (_markShares[i] + end) / 2;
        }

        var before = currentLayer == 1 ? PrimeShare : _cumulativeShare[currentLayer - 2];
        return before + (_cumulativeShare[currentLayer - 1] - before) / 2;
    }

    public string ToJson() => JsonSerializer.Serialize(new Stored(
        _cumulativeShare, PrimeShare, _marks.Select(m => m.Layer).ToArray(), _marks.Select(m => m.Percent).ToArray(), _markShares));

    /// <summary>Reads the current format and the original bare array of per-layer shares.</summary>
    public static FilamentUsageCurve? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        if (json.TrimStart()[0] == '[') return new(JsonSerializer.Deserialize<double[]>(json) ?? [], 0, [], []);
        var stored = JsonSerializer.Deserialize<Stored>(json);
        return stored is null
            ? null
            : new(stored.Layers, stored.Prime, stored.MarkLayers.Zip(stored.MarkPercents, (l, p) => new ProgressMark(l, p)).ToArray(), stored.MarkShares);
    }

    private sealed record Stored(double[] Layers, double Prime, int[] MarkLayers, int[] MarkPercents, double[] MarkShares);
}
