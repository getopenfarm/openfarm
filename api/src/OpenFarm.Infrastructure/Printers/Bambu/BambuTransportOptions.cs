using System.Security.Cryptography.X509Certificates;
using FluentFTP;
using MQTTnet;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Instance-local Infrastructure test seams. Production constructors always use the LAN defaults;
/// these settings are deliberately not exposed through configuration or printer registration.
/// </summary>
internal sealed record BambuTransportOptions
{
    public int MqttPort { get; init; } = 8883;
    public int FtpsPort { get; init; } = 990;
    public int CameraPort { get; init; } = 6000;
    public TimeSpan VerifyTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CameraFrameTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Silence after which the open session is nudged for reports (<c>pushing.start</c>), never torn down.</summary>
    public TimeSpan TelemetryTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>A nudge also re-requests a full report at most this often: P1 firmware is known to choke on frequent pushall.</summary>
    public TimeSpan PushAllMinInterval { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Silence after which the session is assumed dead despite MQTT keepalive, and reconnected.</summary>
    public TimeSpan SilenceReconnectAfter { get; init; } = TimeSpan.FromSeconds(120);
    /// <summary>An upload is given this long plus 1 s per 25 KB (a slow P1 SD card) before it counts as hung.</summary>
    public TimeSpan UploadTimeoutFloor { get; init; } = TimeSpan.FromSeconds(30);
    public Func<IMqttClient> CreateMqttClient { get; init; } = () => new MqttClientFactory().CreateMqttClient();
    public Func<X509Certificate?, bool> ValidateCertificate { get; init; } = BambuCertificateTrust.Validate;
    public Func<PrinterConnectionDetails, Stream, string, IProgress<FtpProgress>?, CancellationToken, Task<FtpStatus>>? UploadFileAsync { get; init; }
}
