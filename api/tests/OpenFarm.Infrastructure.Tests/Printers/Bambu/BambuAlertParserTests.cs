using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuAlertParserTests
{
    [Fact]
    public void KnownAlerts_DecodeMessagesCodesSeverityAndLinks()
    {
        var result = BambuAlertParser.Parse("""{"print":{"hms":[{"attr":50331904,"code":65543}],"print_error":117473286}}""", "P1S");
        var hms = result.Hms.Should().ContainSingle().Subject;
        hms.Code.Should().Be("0300_0100_0001_0007");
        hms.Severity.Should().Be("Error");
        hms.Message.Should().Contain("heatbed");
        hms.TroubleshootingUrl.Should().StartWith("https://wiki.bambulab.com/");
        result.PrintErrors.Should().ContainSingle().Which.Code.Should().Be("0700_8006");
    }

    [Fact]
    public void Deltas_DistinguishAbsentMalformedAndExplicitClear()
    {
        BambuAlertParser.Parse("""{"print":{"mc_percent":50}}""", "P1S").Should().Be((null, null));
        BambuAlertParser.Parse("""{"print":{"hms":"bad","print_error":"bad"}}""", "P1S").Should().Be((null, null));
        BambuAlertParser.Parse("""{"print":{"hms":[{"code":1}],"print_error":0}}""", "P1S").Hms.Should().BeNull();
        var clear = BambuAlertParser.Parse("""{"print":{"hms":[],"print_error":0}}""", "P1S");
        clear.Hms.Should().BeEmpty();
        clear.PrintErrors.Should().BeEmpty();
        BambuAlertParser.Parse("{", "P1S").Should().Be((null, null));
    }

    [Theory]
    [InlineData(131073, "Warning")]
    [InlineData(196609, "Information")]
    [InlineData(262145, "Information")]
    [InlineData(4294967295, "Unknown")]
    public void UnknownCodes_KeepRawCodeAndDecodedSeverity(long code, string severity)
    {
        var result = BambuAlertParser.Parse($"{{\"print\":{{\"hms\":[{{\"attr\":4294967295,\"code\":{code}}}]}}}}", "P1S");
        var alert = result.Hms.Should().ContainSingle().Subject;
        alert.Code.Should().StartWith("FFFF_FFFF_");
        alert.Severity.Should().Be(severity);
        alert.Message.Should().Be("Unknown printer alert.");
    }

    [Fact]
    public void CancellationSentinel_IsInformational()
    {
        var result = BambuAlertParser.Parse("""{"print":{"print_error":50348044}}""", "P1S");
        var alert = result.PrintErrors.Should().ContainSingle().Subject;
        alert.Severity.Should().Be("Information");
        alert.Message.Should().Be("Print cancelled by user.");
    }
}
