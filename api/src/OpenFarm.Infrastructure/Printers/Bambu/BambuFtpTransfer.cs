using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.Security.Authentication;
using FluentFTP;
using FluentFTP.Exceptions;
using Microsoft.Extensions.Logging;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>Passive encrypted FTPS, exact-size verification and bounded per-printer recovery.</summary>
internal sealed partial class BambuFtpTransfer(BambuTransportOptions options, ILogger logger)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UploadLocks = new();

    internal AsyncFtpClient CreateClient(PrinterConnectionDetails details)
    {
        var ftp = new AsyncFtpClient(details.IpAddress, "bblp", details.AccessCode, options.FtpsPort);
        ftp.Config.EncryptionMode = FtpEncryptionMode.Implicit;
        ftp.Config.SslProtocols = SslProtocols.Tls12;
        ftp.Config.InternetProtocolVersions = FtpIpVersion.IPv4;
        // PASVEX uses the control peer's address, including when PASV advertises 0.0.0.0.
        ftp.Config.DataConnectionType = FtpDataConnectionType.PASVEX;
        ftp.Config.DataConnectionEncryption = true;
        ftp.Config.SocketKeepAlive = true;
        ftp.Config.ConnectTimeout = Milliseconds(options.UploadInactivityTimeout);
        ftp.Config.ReadTimeout = Milliseconds(options.UploadInactivityTimeout);
        ftp.Config.DataConnectionConnectTimeout = Milliseconds(options.UploadInactivityTimeout);
        ftp.Config.DataConnectionReadTimeout = Milliseconds(options.UploadInactivityTimeout);
        ftp.Config.DataConnectionWriteTimeout = Milliseconds(options.UploadInactivityTimeout);
        // This is the existing FTPS trust policy. MQTT uses strict-first session-scoped fallback.
        ftp.Config.ValidateAnyCertificate = true;
        return ftp;
    }

    private static int Milliseconds(TimeSpan value) => checked((int)Math.Ceiling(value.TotalMilliseconds));

    internal async Task EnsureAsync(PrinterConnectionDetails details, Stream content, string path,
        IProgress<FtpProgress>? progress, CancellationToken ct)
    {
        var gate = UploadLocks.GetOrAdd($"{details.IpAddress}:{options.FtpsPort}/{details.SerialNumber}", _ => new(1));
        await gate.WaitAsync(ct);
        try
        {
            Exception? failure = null;
            var stage = BambuTransferStage.Connecting;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                idle.CancelAfter(options.UploadInactivityTimeout);
                var token = idle.Token;
                long bytes = 0;
                var lastProgress = Stopwatch.GetTimestamp();
                try
                {
                    stage = BambuTransferStage.Connecting;
                    await using var ftp = CreateClient(details);
                    LogConnecting(logger, details.SerialNumber, attempt, path, content.Length);
                    await ftp.Connect(token);
                    Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp());
                    idle.CancelAfter(options.UploadInactivityTimeout);
                    stage = BambuTransferStage.CheckingSize;
                    if (await HasExactSizeAsync(ftp, path, content.Length, token))
                    {
                        LogExisting(logger, details.SerialNumber, path);
                        return;
                    }
                    Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp());
                    idle.CancelAfter(options.UploadInactivityTimeout);
                    stage = BambuTransferStage.Uploading;
                    content.Position = 0;
                    var reported = new InlineProgress<FtpProgress>(p =>
                    {
                        if (p.TransferredBytes > Interlocked.Read(ref bytes))
                        {
                            Interlocked.Exchange(ref bytes, p.TransferredBytes);
                            Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp());
                            idle.CancelAfter(options.UploadInactivityTimeout);
                        }
                        progress?.Report(p);
                    });
                    var result = await BambuLanConnector.UploadNewFileAsync(ftp, content, path, token, reported);
                    if (result != FtpStatus.Success) throw new IOException($"Printer file upload did not succeed ({result}).");
                    Interlocked.Exchange(ref lastProgress, Stopwatch.GetTimestamp());
                    idle.CancelAfter(options.UploadInactivityTimeout);
                    stage = BambuTransferStage.Verifying;
                    if (!await HasExactSizeAsync(ftp, path, content.Length, token))
                        throw new IOException("Printer upload verification failed: file missing or size mismatch.");
                    LogVerified(logger, details.SerialNumber, path, content.Length);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    failure = ex is OperationCanceledException
                        ? new TimeoutException($"Printer FTPS {stage} made no progress for {options.UploadInactivityTimeout.TotalSeconds:0.###} s ({bytes}/{content.Length} bytes).", ex)
                        : ex;
                    var code = ReplyCode(ex);
                    LogFailed(logger, details.SerialNumber, attempt, stage, code, bytes, content.Length, Stopwatch.GetElapsedTime(Interlocked.Read(ref lastProgress)).TotalSeconds, failure);
                    if (code is "550" or "552" or "553")
                        throw new BambuTransferException(stage, StorageMessage(code), failure);
                }

                // Cancellation closes the failed connection before opening a fresh control session.
                // Even the last attempt may have written everything and merely lost the final 226 reply.
                if (stage is BambuTransferStage.Uploading or BambuTransferStage.Verifying)
                {
                    try
                    {
                        using var verify = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        verify.CancelAfter(options.UploadInactivityTimeout);
                        await using var ftp = CreateClient(details);
                        await ftp.Connect(verify.Token);
                        if (await HasExactSizeAsync(ftp, path, content.Length, verify.Token))
                        {
                            LogRecovered(logger, details.SerialNumber, path);
                            return;
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        LogRecoveryFailed(logger, details.SerialNumber, ex);
                    }
                }
                if (attempt < 3) await Task.Delay(options.UploadRetryDelay * attempt, ct);
            }
            throw new BambuTransferException(stage, $"Printer file upload failed after 3 attempts: {failure?.Message}", failure);
        }
        finally { gate.Release(); }
    }

    internal static async Task<bool> HasExactSizeAsync(AsyncFtpClient ftp, string path, long length, CancellationToken ct)
    {
        // Direct SIZE never falls back to NLST, whose unrelated filenames can fail sanitization.
        var type = await ftp.Execute("TYPE I", ct);
        if (!type.Success) throw new FtpCommandException(type);
        var reply = await ftp.Execute($"SIZE {path}", ct);
        if (reply.Code == "550") return false; // Normal absence before upload.
        if (!reply.Success) throw new FtpCommandException(reply);
        return long.TryParse(reply.Message, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
            && size == length && size > 0;
    }

    internal static string? ReplyCode(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
            if (current is FtpCommandException command) return command.CompletionCode;
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Serial} FTPS attempt {Attempt}: connecting for {Path} ({Length} bytes).")]
    private static partial void LogConnecting(ILogger logger, string serial, int attempt, string path, long length);
    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Serial} FTPS verified existing file {Path}.")]
    private static partial void LogExisting(ILogger logger, string serial, string path);
    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Serial} FTPS verified {Path} ({Length} bytes).")]
    private static partial void LogVerified(ILogger logger, string serial, string path, long length);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {Serial} FTPS attempt {Attempt} failed at {Stage}, reply {Reply}, bytes {Bytes}/{Length}, last progress {IdleSeconds:0.###} s ago.")]
    private static partial void LogFailed(ILogger logger, string serial, int attempt, BambuTransferStage stage, string? reply, long bytes, long length, double idleSeconds, Exception exception);
    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Serial} FTPS recovered {Path} after transfer failure; exact size verified.")]
    private static partial void LogRecovered(ILogger logger, string serial, string path);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {Serial} FTPS recovery verification failed.")]
    private static partial void LogRecoveryFailed(ILogger logger, string serial, Exception exception);

    private static string StorageMessage(string code) => code switch
    {
        "552" => "Printer storage is full (FTP 552). Free space on the microSD card or USB drive.",
        "553" => "Printer storage is unavailable (FTP 553). Check that its microSD card or USB drive is inserted.",
        _ => "Printer rejected the file (FTP 550). Check that its microSD card or USB drive is writable.",
    };
}
