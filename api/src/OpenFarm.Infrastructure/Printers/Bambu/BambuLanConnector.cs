using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Formatter;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Real Bambu LAN connector: MQTT 3.1.1 over TLS on port 8883, username <c>bblp</c> / password =
/// the printer's LAN access code, topics <c>device/{serial}/report</c> (subscribe) and
/// <c>device/{serial}/request</c> (publish), per research.md §1. First validated against real
/// hardware (a Bambu A1) via quickstart.md's manual physical-hardware validation section — that pass
/// found and fixed the TLS trust-store root/intermediate bug (<see cref="BambuCertificateTrust"/>)
/// and this class's missing MQTT protocol-version pin (research.md §1's two post-implementation
/// notes); see <c>Printers/Fixtures/README.md</c> for the fixture-replay corpus used elsewhere.
/// </summary>
public sealed partial class BambuLanConnector : IPrinterConnector
{
    private const string MqttUsername = "bblp";
    private const string FtpsCacheDirectory = "/cache";
    private static readonly string[] CommandResponseFields = ["command", "sequence_id", "result", "reason", "errno"];

    private static readonly JsonSerializerOptions IgnoreNullOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly ILogger<BambuLanConnector> _logger;
    private readonly BambuTransportOptions _options;

    public BambuLanConnector(ILogger<BambuLanConnector> logger)
        : this(logger, new BambuTransportOptions())
    {
    }

    internal BambuLanConnector(ILogger<BambuLanConnector> logger, BambuTransportOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task<bool> VerifyAsync(PrinterConnectionDetails details, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.VerifyTimeout);
        await using var session = BambuSessionManager.Shared.Acquire(details, _options, _logger);
        try
        {
            await session.ReadyAsync(timeout.Token);
            if (session.RefreshRequired)
            {
                await session.PublishAsync("""{"pushing":{"sequence_id":"0","command":"pushall"}}""", false, timeout.Token);
                await session.PublishAsync("""{"info":{"sequence_id":"0","command":"get_version"}}""", false, timeout.Token);
            }
            bool info = false, print = false;
            await foreach (var message in session.Messages.Reader.ReadAllAsync(timeout.Token))
            {
                if (message.Payload is not { } payload) continue;
                info |= BambuMqttMessageParser.IsGetVersionResponse(payload);
                print |= BambuMqttMessageParser.IsPrintReport(payload);
                if (info && print) return true;
            }
        }
        catch (Exception ex) { LogVerifyFailed(_logger, details.IpAddress, ex); }
        return false;
    }

    public async IAsyncEnumerable<PrinterConnectorEvent> ConnectAsync(
        PrinterConnectionDetails details, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var session = BambuSessionManager.Shared.Acquire(details, _options, _logger);
        if (session.RefreshRequired)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.CommandTimeout);
            await session.ReadyAsync(timeout.Token);
            await session.PublishAsync("""{"pushing":{"sequence_id":"0","command":"pushall"}}""", false, timeout.Token);
            await session.PublishAsync("""{"info":{"sequence_id":"0","command":"get_version"}}""", false, timeout.Token);
        }
        await foreach (var message in session.Messages.Reader.ReadAllAsync(cancellationToken))
        {
            if (message.Started || message.Ended)
            {
                yield return new(message.ReceivedAtUtc, ConnectionStarted: message.Started, ConnectionEnded: message.Ended);
                continue;
            }
            if (message.Payload is not { } payload) continue;
            if (TryCommandResponse(payload) is { } commandResponse)
            {
                LogCommandResponse(_logger, details.SerialNumber, commandResponse);
                continue;
            }
            if (!BambuMqttMessageParser.IsPrintReport(payload))
            {
                if (BambuMqttMessageParser.TryParseFirmwareVersion(payload) is { } firmware) LogFirmware(_logger, details.SerialNumber, firmware);
                continue;
            }
            var state = BambuMqttMessageParser.TryParseGcodeState(payload, out var reported) ? reported : null;
            var telemetry = BambuMqttMessageParser.TryParseTelemetry(payload);
            var alerts = BambuAlertParser.Parse(payload, details.ModelCode);
            yield return new(message.ReceivedAtUtc, GcodeState: state, Telemetry: telemetry, HmsAlerts: alerts.Hms, PrintErrors: alerts.PrintErrors);
        }
    }

    private static string? TryCommandResponse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("print", out var print)
                && print.ValueKind == JsonValueKind.Object
                && print.TryGetProperty("command", out var command)
                && command.ValueKind == JsonValueKind.String
                && command.GetString() is not (null or "push_status"))
                return string.Join(", ", CommandResponseFields
                    .Where(key => print.TryGetProperty(key, out _))
                    .Select(key => $"{key}={print.GetProperty(key)}"));
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// FTPS upload (port 990, implicit TLS — Bambu's LAN-mode FTP server convention) followed by an
    /// MQTT <c>print.project_file</c> publish (research.md R5) on the printer's open
    /// <see cref="ConnectAsync"/> session (see <see cref="PublishCommandAsync"/>).
    /// </summary>
    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping, CancellationToken cancellationToken) =>
        StartJobAsync(details, fileContent, jobName, plate, feedMapping, null, cancellationToken);

    public Task StartJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
        IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken) =>
        StartSelectedJobAsync(details, fileContent, jobName, plate, feedMapping, [], progress, cancellationToken);

    public Task SkipObjectsAsync(PrinterConnectionDetails details, IReadOnlyList<string> objectIds, CancellationToken ct) =>
        PublishCommandAsync(details, JsonSerializer.Serialize(new { print = new { sequence_id = "0", command = "skip_objects", obj_list = objectIds.Select(id => int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)).ToArray() } }), ct);

    public Task StartSelectedJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
        IReadOnlyList<string> excludedObjects, CancellationToken cancellationToken) =>
        StartSelectedJobAsync(details, fileContent, jobName, plate, feedMapping, excludedObjects, null, cancellationToken);

    public async Task StartSelectedJobAsync(PrinterConnectionDetails details, Stream fileContent, string jobName, int plate, FilamentFeedMapping? feedMapping,
        IReadOnlyList<string> excludedObjects, IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
    {
        using var buffered = new MemoryStream();
        await fileContent.CopyToAsync(buffered, cancellationToken);
        var input = buffered.ToArray();
        var (remotePath, md5) = await UploadProjectAsync(details, input, jobName, plate, progress, cancellationToken);

        // Spelled out even for the external spool alone, so the printer never falls back to a tray of its own choosing.
        (bool UseAms, int[] Trays, (int AmsId, int SlotId)[] Targets)? mapping = feedMapping is null ? null : EncodeAmsMapping(feedMapping);
        var payload = JsonSerializer.Serialize(new
        {
            print = new
            {
                sequence_id = "0",
                command = "project_file",
                skip_objects = excludedObjects.Count == 0 ? null : excludedObjects.Select(id => int.Parse(id, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
                param = BambuPrintFile.PlatePath(plate),
                project_id = "0",
                profile_id = "0",
                task_id = "0",
                subtask_id = "0",
                subtask_name = jobName,
                file = remotePath.TrimStart('/'),
                url = $"ftp://{remotePath.TrimStart('/')}",
                md5,
                timelapse = false,
                bed_type = "auto",
                bed_leveling = true,
                flow_cali = false,
                vibration_cali = false,
                layer_inspect = false,
                use_ams = mapping?.UseAms ?? false,
                ams_mapping = mapping?.Trays,
                ams_mapping2 = mapping?.Targets.Select(t => new { ams_id = t.AmsId, slot_id = t.SlotId }),
            },
        }, IgnoreNullOptions);
        progress?.Report(new PrintSendProgress(PrintSendStage.Starting));
        await PublishCommandAsync(details, payload, cancellationToken);
    }

    private async Task<(string RemotePath, string Md5)> UploadProjectAsync(
        PrinterConnectionDetails details, byte[] input, string jobName, int plate, IProgress<PrintSendProgress>? progress, CancellationToken cancellationToken)
    {
        using var project = new MemoryStream(BambuPrintFile.PrepareProject(input, jobName, plate));
#pragma warning disable CA5351 // MD5 here is a Bambu protocol-mandated file-integrity checksum
        // (project_file's own "md5" field), not a security use.
        var md5 = Convert.ToHexStringLower(await MD5.HashDataAsync(project, cancellationToken));
#pragma warning restore CA5351
        project.Position = 0;

        var hash = Convert.ToHexStringLower(SHA256.HashData(project.ToArray()));
        var stem = System.Text.RegularExpressions.Regex.Replace(jobName, "[^a-zA-Z0-9_() -]", "_").Trim(' ', '.');
        if (stem.Length > 60) stem = stem[..60].Trim();
        if (string.IsNullOrEmpty(stem)) stem = "print";
        var remotePath = $"/openfarm-{stem}-{hash[..32]}.gcode.3mf";
        var started = Stopwatch.GetTimestamp();
        var uploadProgress = progress is null ? null
            : new InlineProgress<FtpProgress>(p => progress.Report(new PrintSendProgress(PrintSendStage.Uploading, (int)Math.Clamp(p.Progress, 0, 100))));
        if (_options.UploadFileAsync is { } substitute)
        {
            // Test seam substitutes the complete verified transfer, not production's FTP client.
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idle.CancelAfter(_options.UploadInactivityTimeout);
            long transferred = 0;
            var reported = new InlineProgress<FtpProgress>(p =>
            {
                if (p.TransferredBytes > transferred)
                {
                    transferred = p.TransferredBytes;
                    idle.CancelAfter(_options.UploadInactivityTimeout);
                }
                uploadProgress?.Report(p);
            });
            try
            {
                if (await substitute(details, project, remotePath, reported, idle.Token) != FtpStatus.Success)
                    throw new IOException("Printer file upload did not succeed.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Printer file upload made no progress before the inactivity timeout.");
            }
        }
        else
        {
            await new BambuFtpTransfer(_options, _logger).EnsureAsync(details, project, remotePath, uploadProgress, cancellationToken);
        }

        var seconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var sizeKb = project.Length / 1024d;
        var kbPerSecond = sizeKb / Math.Max(seconds, 0.001);
        LogUploaded(_logger, details.SerialNumber, sizeKb, seconds, kbPerSecond);
        return (remotePath, md5);
    }

    /// <summary>
    /// Bambu Studio's encoding of a feed mapping. <c>ams_mapping</c> holds absolute tray ids — <c>unit * 4 + slot</c> for a
    /// 4-slot AMS or AMS Lite, the unit id for a single-slot AMS HT (ids from 128), -1 for an unused or external filament;
    /// <c>ams_mapping2</c> holds <c>{ams_id, slot_id}</c>, where the external spool is <c>{255, 0}</c> and unmapped is
    /// <c>{255, 255}</c>. <c>use_ams</c> is true when any filament comes from an AMS.
    /// </summary>
    internal static (bool UseAms, int[] Trays, (int AmsId, int SlotId)[] Targets) EncodeAmsMapping(FilamentFeedMapping mapping)
    {
        const int firstSingleSlotUnit = 128;
        const int unmapped = 255;
        var trays = new int[mapping.Tools.Count];
        var targets = new (int AmsId, int SlotId)[mapping.Tools.Count];
        for (var tool = 0; tool < mapping.Tools.Count; tool++)
        {
            (trays[tool], targets[tool]) = mapping.Tools[tool] switch
            {
                null => (-1, (unmapped, unmapped)),
                { IsMultiMaterialUnit: false } => (-1, (unmapped, 0)),
                { UnitIndex: >= firstSingleSlotUnit } slot => (slot.UnitIndex, (slot.UnitIndex, 0)),
                { } slot => (slot.UnitIndex * 4 + slot.SlotPosition, (slot.UnitIndex, slot.SlotPosition)),
            };
        }

        return (mapping.Tools.Any(t => t?.IsMultiMaterialUnit == true), trays, targets);
    }

    internal static Task<FtpStatus> UploadNewFileAsync(
        AsyncFtpClient ftp, Stream content, string remotePath, CancellationToken cancellationToken, IProgress<FtpProgress>? progress = null) =>
        // SIZE handles verification separately; avoid directory-listing existence checks.
        ftp.UploadStream(content, remotePath, existsMode: FtpRemoteExists.NoCheck, progress: progress, token: cancellationToken);

    /// <inheritdoc />
    public Task PauseJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"pause"}}""", cancellationToken);

    /// <inheritdoc />
    public Task ResumeJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"resume"}}""", cancellationToken);

    /// <inheritdoc />
    public Task StopJobAsync(PrinterConnectionDetails details, CancellationToken cancellationToken) =>
        PublishCommandAsync(details, """{"print":{"sequence_id":"0","command":"stop"}}""", cancellationToken);

    /// <inheritdoc />
    public async Task<byte[]?> FetchPrintFileAsync(PrinterConnectionDetails details, string? jobName, string? gcodeFile, CancellationToken cancellationToken)
    {
        var candidates = CandidateFilePaths(jobName, gcodeFile).Distinct().ToList();
        if (candidates.Count == 0) return null;

        await using var ftp = new BambuFtpTransfer(_options, _logger).CreateClient(details);
        await ftp.Connect(cancellationToken);
        foreach (var path in candidates)
        {
            // No FileExists first: it falls back to listing the folder and rejects the whole check when any unrelated file there
            // has a name FluentFTP treats as a path-traversal attempt (e.g. "...Parametric....gcode.bbl"), which hid every
            // file under /cache on some printers. A missing file answers 550 at once.
            byte[]? bytes;
            try
            {
                bytes = await ftp.DownloadBytes(path, cancellationToken);
            }
            catch (FtpException ex) when (IsFileMissing(ex))
            {
                continue;
            }

            if (bytes is { Length: > 0 }) return bytes;
        }

        return null;
    }

    /// <summary>The server's 550 reply to a download of a file that is not there (or not readable).</summary>
    internal static bool IsFileMissing(FtpException exception) =>
        exception.InnerException is FtpCommandException { CompletionCode: "550" };

    /// <summary>
    /// Where a print the printer started itself is likely stored. Bambu Studio and Handy leave the project as
    /// <c>/cache/&lt;name&gt;.gcode.3mf</c> (or <c>.3mf</c>) while <c>gcode_file</c> usually names the plate inside it, so the
    /// reported name leads; a <c>gcode_file</c> that is itself a file on the card (SD prints) is tried too. Unverified on hardware.
    /// </summary>
    internal static IEnumerable<string> CandidateFilePaths(string? jobName, string? gcodeFile)
    {
        const string card = "/sdcard";
        if (!string.IsNullOrWhiteSpace(gcodeFile) && gcodeFile.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
            yield return gcodeFile.StartsWith(card, StringComparison.Ordinal) ? gcodeFile[card.Length..] : gcodeFile;
        if (!string.IsNullOrWhiteSpace(jobName) && !jobName.Contains('/', StringComparison.Ordinal))
            foreach (var name in new[] { $"{jobName}.gcode.3mf", $"{jobName}.3mf" })
            {
                yield return $"{FtpsCacheDirectory}/{name}";
                yield return $"/{name}";
            }

        if (!string.IsNullOrWhiteSpace(gcodeFile) && gcodeFile.StartsWith(card + "/", StringComparison.Ordinal)
            && gcodeFile.EndsWith(".gcode", StringComparison.OrdinalIgnoreCase))
            yield return gcodeFile[card.Length..];
    }

    /// <summary>
    /// Publishes one command on the printer's open telemetry session, like Bambu Studio and every reference client. Only
    /// when none is open does it connect, publish and disconnect — mirroring <see cref="VerifyAsync"/>'s shape.
    /// </summary>
    private async Task PublishCommandAsync(PrinterConnectionDetails details, string payload, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.CommandTimeout);
        await using var session = BambuSessionManager.Shared.Acquire(details, _options, _logger);
        await session.PublishAsync(payload, true, timeout.Token);
    }

    internal static MqttClientOptions BuildClientOptions(PrinterConnectionDetails details, BambuTransportOptions? options = null, Func<System.Security.Cryptography.X509Certificates.X509Certificate?, bool>? validate = null) =>
        new MqttClientOptionsBuilder()
            .WithTcpServer(details.IpAddress, options?.MqttPort ?? 8883)
            .WithCredentials(MqttUsername, details.AccessCode)
            // MQTT keepalive detects transport failure; telemetry silence alone never tears it down.
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(options?.ConnectTimeout ?? TimeSpan.FromSeconds(10))
            .WithCleanSession()
            // MQTTnet defaults to MQTT 5, but Bambu's LAN broker only speaks v3.1.1 (research.md §1;
            // confirmed against pybambu's mqtt.Client(protocol=mqtt.MQTTv311)) — a v5 CONNECT gets a
            // successful-looking CONNACK but the broker then can't parse the v5-shaped SUBSCRIBE that
            // follows and silently closes the socket, surfacing as MqttClientNotConnectedException on
            // the very next call.
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithClientId($"openfarm-{details.SerialNumber}")
            .WithTlsOptions(tls => tls
                .UseTls()
                .WithTargetHost(details.SerialNumber)
                // Sessions first validate the bundled Bambu chain and expected serial. Only recognized
                // chain failures enable the logged, instance-local certificate compatibility fallback.
                .WithCertificateValidationHandler(args => (validate ?? options?.ValidateCertificate ?? BambuCertificateTrust.Validate)(args.Certificate))
                .WithSslProtocols(System.Security.Authentication.SslProtocols.Tls12))
            .Build();

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Serial} command response: {Response}")]
    private static partial void LogCommandResponse(ILogger logger, string serial, string response);

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {SerialNumber} firmware {Firmware}.")]
    private static partial void LogFirmware(ILogger logger, string serialNumber, string firmware);

    [LoggerMessage(Level = LogLevel.Information, Message = "Uploaded {SizeKb:0} KB to printer {SerialNumber} in {Seconds:0.0} s ({KbPerSecond:0} KB/s).")]
    private static partial void LogUploaded(ILogger logger, string serialNumber, double sizeKb, double seconds, double kbPerSecond);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} failed.")]
    private static partial void LogVerifyFailed(ILogger logger, string ipAddress, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verify connection to {IpAddress} timed out.")]
    private static partial void LogVerifyTimedOut(ILogger logger, string ipAddress);
}
