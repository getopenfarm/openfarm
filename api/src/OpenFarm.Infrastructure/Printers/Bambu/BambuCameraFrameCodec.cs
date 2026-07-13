using System.Buffers.Binary;
using System.Text;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Pure byte-framing logic for the Bambu P1/A1 <c>CAMERA_IMAGE</c> binary protocol (research.md
/// §4) — building the auth blob and validating a received frame payload. Never touches a socket;
/// that's <see cref="BambuCameraRelay"/>'s job. Test-after via fixture-replay, per the
/// constitution's named exemption for connector code.
/// </summary>
public static class BambuCameraFrameCodec
{
    private const string Username = "bblp";
    public const int HeaderSize = 16;
    private const int UsernameFieldSize = 32;
    private const int AccessCodeFieldSize = 32;
    public const int AuthBlobSize = HeaderSize + UsernameFieldSize + AccessCodeFieldSize;

    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] JpegEnd = [0xFF, 0xD9];

    /// <summary>
    /// 4× little-endian <c>uint32</c> (<c>0x40</c>, <c>0x3000</c>, <c>0</c>, <c>0</c>) + <c>"bblp"</c>
    /// NUL-padded to 32 bytes + the LAN access code NUL-padded to 32 bytes (research.md §4).
    /// </summary>
    public static byte[] BuildAuthBlob(string accessCode)
    {
        var blob = new byte[AuthBlobSize];
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(0, 4), 0x40);
        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(4, 4), 0x3000);
        // Bytes 8:15 stay zero.
        Encoding.ASCII.GetBytes(Username, blob.AsSpan(HeaderSize, UsernameFieldSize));
        Encoding.ASCII.GetBytes(accessCode, blob.AsSpan(HeaderSize + UsernameFieldSize, AccessCodeFieldSize));
        return blob;
    }

    /// <summary>
    /// Bytes 0:3 of a 16-byte frame header, little-endian — the source only reads 3 of the 4 size
    /// bytes, capping representable size at 0xFFFFFF (research.md §4).
    /// </summary>
    public static int ParsePayloadSize(ReadOnlySpan<byte> header) =>
        header[0] | (header[1] << 8) | (header[2] << 16);

    /// <summary>Validates the payload starts with the JPEG SOI/APP0 marker and ends with the EOI marker.</summary>
    public static bool IsValidJpegFrame(ReadOnlySpan<byte> payload) =>
        payload.Length >= JpegStart.Length + JpegEnd.Length
        && payload[..JpegStart.Length].SequenceEqual(JpegStart)
        && payload[^JpegEnd.Length..].SequenceEqual(JpegEnd);
}
