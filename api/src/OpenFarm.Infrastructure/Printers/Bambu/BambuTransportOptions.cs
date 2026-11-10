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
    // Allow strict TLS, the first reconnect delay, and the certificate-compatibility retry.
    public TimeSpan VerifyTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(5);
    public bool CheckCertificateSerial { get; init; } = true;
    public bool AllowCertificateFallback { get; init; } = true;
    public TimeSpan UploadInactivityTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan UploadRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan CameraFrameTimeout { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Silence after which a connected session asks for reports again; under the 90 s offline sweep, and no more often:
    /// P1 firmware chokes on frequent pushall.</summary>
    public TimeSpan ReportSilenceNudge { get; init; } = TimeSpan.FromSeconds(60);
    public Func<IMqttClient> CreateMqttClient { get; init; } = () => new MqttClientFactory().CreateMqttClient();
    public Func<X509Certificate?, bool> ValidateCertificate { get; init; } = BambuCertificateTrust.Validate;
    public Func<PrinterConnectionDetails, Stream, string, IProgress<FtpProgress>?, CancellationToken, Task<FtpStatus>>? UploadFileAsync { get; init; }
}
