namespace OpenFarm.Infrastructure.Printers;

/// <summary>A whole JPEG: the SOI marker, then any segment marker (JFIF APP0, EXIF APP1, a bare DQT, ...), and the EOI marker last.</summary>
internal static class JpegFrame
{
    public static bool IsValid(ReadOnlySpan<byte> payload) =>
        payload.Length >= 5
        && payload[0] == 0xFF && payload[1] == 0xD8 && payload[2] == 0xFF
        && payload[^2] == 0xFF && payload[^1] == 0xD9;
}
