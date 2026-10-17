using System.Text.Json;

namespace OpenFarm.Domain.Production;

/// <summary>
/// How a job line's filament is spread over the print: the cumulative share (0..1) of its total used by the end of
/// each layer, read from the dispatched G-code. Lets a failed or cancelled multi-colour print deduct what each colour
/// really used by the layer it stopped at, instead of assuming every colour is used evenly.
/// </summary>
public sealed class FilamentUsageCurve
{
    private readonly double[] _cumulativeShare;

    private FilamentUsageCurve(double[] cumulativeShare) => _cumulativeShare = cumulativeShare;

    public int LayerCount => _cumulativeShare.Length;

    /// <summary>
    /// Builds the curve for one line from the cumulative filament per layer (any unit) of each tool it draws from,
    /// summed. Null when those tools extruded nothing, so the caller falls back to an even spread.
    /// </summary>
    public static FilamentUsageCurve? Create(IEnumerable<double[]> cumulativeByTool)
    {
        var tools = cumulativeByTool.Where(c => c.Length > 0).ToList();
        if (tools.Count == 0) return null;
        var layers = tools.Max(c => c.Length);
        var summed = new double[layers];
        foreach (var tool in tools)
            for (var i = 0; i < layers; i++)
                summed[i] += tool[Math.Min(i, tool.Length - 1)];
        var total = summed[^1];
        if (total <= 0) return null;
        return new FilamentUsageCurve(summed.Select(v => Math.Round(Math.Clamp(v / total, 0, 1), 6)).ToArray());
    }

    /// <summary>
    /// Share used when stopped while printing <paramref name="currentLayer"/> (1-based, as the printer reports it): everything
    /// through the previous layer plus half of the current one, since the stop landed somewhere inside it.
    /// </summary>
    public double ShareAt(int currentLayer)
    {
        if (currentLayer <= 0) return 0;
        if (currentLayer > LayerCount) return 1;
        var before = currentLayer == 1 ? 0 : _cumulativeShare[currentLayer - 2];
        return before + (_cumulativeShare[currentLayer - 1] - before) / 2;
    }

    public string ToJson() => JsonSerializer.Serialize(_cumulativeShare);

    public static FilamentUsageCurve? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : new(JsonSerializer.Deserialize<double[]>(json) ?? []);
}
