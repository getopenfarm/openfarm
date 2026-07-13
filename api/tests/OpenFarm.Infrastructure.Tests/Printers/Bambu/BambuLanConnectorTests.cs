using FluentAssertions;
using MQTTnet.Formatter;
using OpenFarm.Application.Printers;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>
/// Regression coverage for the MQTT protocol-version bug: MQTTnet defaults to MQTT 5, but Bambu's
/// LAN broker only speaks v3.1.1 — a v5 CONNECT gets a successful-looking CONNACK, then the broker
/// silently drops the socket on the next (v5-shaped) SUBSCRIBE packet.
/// </summary>
public class BambuLanConnectorTests
{
    [Fact]
    public void BuildClientOptions_UsesMqttV311AndSetsAClientId()
    {
        var details = new PrinterConnectionDetails("192.168.1.50", "SERIAL123", "12345678");

        var options = BambuLanConnector.BuildClientOptions(details);

        options.ProtocolVersion.Should().Be(MqttProtocolVersion.V311);
        options.ClientId.Should().NotBeNullOrEmpty();
    }
}
