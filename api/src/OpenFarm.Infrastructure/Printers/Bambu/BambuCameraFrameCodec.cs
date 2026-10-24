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

    /// <summary>
    /// Sanity ceiling on top of the wire format's own 0xFFFFFF (~16MB) cap — a single chamber JPEG
    /// frame is a few hundred KB at most, so a header claiming several MB is either protocol
    /// desync or a malfunctioning/compromised printer, not a legitimate frame. Enforced by
    /// <see cref="BambuCameraRelay"/> before allocating the payload buffer.
    /// </summary>
    public const int MaxPayloadSize = 4 * 1024 * 1024;

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

    /// <summary>Validates the payload is a whole JPEG (<see cref="JpegFrame"/>).</summary>
    public static bool IsValidJpegFrame(ReadOnlySpan<byte> payload) => JpegFrame.IsValid(payload);
}
