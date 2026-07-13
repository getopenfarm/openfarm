using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>
/// Fixture-replay tests (test-after, per the constitution's named exemption for connector code) —
/// no physical/simulated Bambu camera was reachable from this sandbox, so the frame fixture is
/// hand-constructed protocol-accurate bytes matching research.md §4's documented framing exactly,
/// not a captured frame. See <c>Printers/Fixtures/README.md</c>.
/// </summary>
public class BambuCameraFrameCodecTests
{
    private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Printers", "Fixtures");

    [Fact]
    public void BuildAuthBlob_ProducesAn80ByteBlobWithHeaderUsernameAndAccessCode()
    {
        var blob = BambuCameraFrameCodec.BuildAuthBlob("12345678");

        blob.Should().HaveCount(80);
        blob[0].Should().Be(0x40);
        blob[4].Should().Be(0x00);
        blob[5].Should().Be(0x30);
        blob.Skip(16).Take(4).Should().Equal("bblp"u8.ToArray());
        blob.Skip(16 + 4).Take(28).Should().AllSatisfy(b => b.Should().Be(0));
        blob.Skip(48).Take(8).Should().Equal("12345678"u8.ToArray());
        blob.Skip(48 + 8).Take(24).Should().AllSatisfy(b => b.Should().Be(0));
    }

    [Fact]
    public void ParsePayloadSize_ReadsThreeByteLittleEndianFromHeader()
    {
        byte[] header = [0x34, 0x12, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        BambuCameraFrameCodec.ParsePayloadSize(header).Should().Be(0x1234);
    }

    [Fact]
    public void ParsePayloadSize_CanExceedMaxPayloadSize_SoCallersMustEnforceTheCeilingThemselves()
    {
        // The wire format's own cap (0xFFFFFF, ~16MB — only 3 of the header's 4 size bytes are
        // read) sits well above MaxPayloadSize, confirming BambuCameraRelay's own ceiling check
        // is load-bearing and not redundant with anything the codec already rejects.
        byte[] header = [0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        BambuCameraFrameCodec.ParsePayloadSize(header).Should().BeGreaterThan(BambuCameraFrameCodec.MaxPayloadSize);
    }

    [Fact]
    public void IsValidJpegFrame_WithRealFrameFixture_ReturnsTrue()
    {
        var frame = File.ReadAllBytes(Path.Combine(FixturesDirectory, "sample-camera-frame.jpg"));

        BambuCameraFrameCodec.IsValidJpegFrame(frame).Should().BeTrue();
    }

    [Fact]
    public void IsValidJpegFrame_WithMissingStartMagicBytes_ReturnsFalse()
    {
        byte[] frame = [0x00, 0x00, 0x00, 0x00, 0xFF, 0xD9];

        BambuCameraFrameCodec.IsValidJpegFrame(frame).Should().BeFalse();
    }

    [Fact]
    public void IsValidJpegFrame_WithMissingEndMagicBytes_ReturnsFalse()
    {
        byte[] frame = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00];

        BambuCameraFrameCodec.IsValidJpegFrame(frame).Should().BeFalse();
    }

    [Fact]
    public void IsValidJpegFrame_WithTooShortPayload_ReturnsFalse()
    {
        byte[] frame = [0xFF, 0xD8];

        BambuCameraFrameCodec.IsValidJpegFrame(frame).Should().BeFalse();
    }
}
