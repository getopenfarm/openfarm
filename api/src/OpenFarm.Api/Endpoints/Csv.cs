using System.Globalization;
using System.Text;

namespace OpenFarm.Api.Endpoints;

/// <summary>
/// RFC 4180 CSV for spreadsheets: CRLF rows, UTF-8 with a BOM so Excel reads the encoding, invariant numbers and UTC ISO 8601 times.
/// Text a spreadsheet would run as a formula gets a leading <c>'</c>.
/// </summary>
internal static class Csv
{
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var text = new StringBuilder();
        foreach (var fields in rows.Prepend(header))
        {
            text.AppendJoin(',', fields.Select(Field)).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Field(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTimeOffset time => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0], StringComparison.Ordinal))
        {
            text = "'" + text;
        }

        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : text;
    }
}
