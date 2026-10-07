using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>Minimal implicit-FTPS peer exercising FluentFTP against printer directory entries.</summary>
internal sealed class BambuFtpsHarness : IAsyncDisposable
{
    internal const string ExistingFileName = "1_Parametric Text Test Lucida H5 D2.5.stl + Parametric Text Test Helvetica H5 D2.5.stl + Parametric....gcode.bbl";
    private readonly LoopbackTlsServer _server;

    public BambuFtpsHarness() => _server = new LoopbackTlsServer(HandleSessionAsync);

    public int Port => _server.Port;
    public ConcurrentQueue<string> Commands { get; } = new();
    public byte[]? UploadedBytes { get; private set; }
    public string? UploadedPath { get; private set; }

    private async Task HandleSessionAsync(SslStream control, CancellationToken token)
    {
        using var reader = new StreamReader(control, Encoding.UTF8, leaveOpen: true);
        await using var writer = new StreamWriter(control, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
        TcpListener? dataListener = null;
        var encryptData = false;
        try
        {
            await writer.WriteLineAsync("220 Bambu test FTP".AsMemory(), token);
            while (await reader.ReadLineAsync(token) is { } line)
            {
                Commands.Enqueue(line);
                var command = line.Split(' ', 2)[0];
                switch (command)
                {
                    case "USER":
                        await writer.WriteLineAsync("331 Password required".AsMemory(), token);
                        break;
                    case "PASS":
                        await writer.WriteLineAsync("230 Logged in".AsMemory(), token);
                        break;
                    case "FEAT":
                        // No SIZE/MDTM support, so FileExists falls back to NLST as in the report.
                        await writer.WriteLineAsync("211 No features".AsMemory(), token);
                        break;
                    case "SYST":
                        await writer.WriteLineAsync("215 UNIX Type: L8".AsMemory(), token);
                        break;
                    case "PWD":
                        await writer.WriteLineAsync("257 \"/\"".AsMemory(), token);
                        break;
                    case "PROT":
                        encryptData = line.EndsWith(" P", StringComparison.Ordinal);
                        await writer.WriteLineAsync("200 Protection set".AsMemory(), token);
                        break;
                    case "PBSZ":
                    case "TYPE":
                    case "OPTS":
                    case "CWD":
                    case "NOOP":
                        await writer.WriteLineAsync("200 OK".AsMemory(), token);
                        break;
                    case "EPSV":
                    case "PASV":
                        dataListener?.Stop();
                        dataListener = new TcpListener(IPAddress.Loopback, 0);
                        dataListener.Start();
                        var port = ((IPEndPoint)dataListener.LocalEndpoint).Port;
                        await writer.WriteLineAsync((command == "EPSV"
                            ? $"229 Entering Extended Passive Mode (|||{port}|)"
                            : $"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})").AsMemory(), token);
                        break;
                    case "NLST":
                    case "STOR":
                        if (dataListener is null) throw new InvalidDataException("No passive data listener.");
                        await writer.WriteLineAsync("150 Opening data connection".AsMemory(), token);
                        using (var client = await dataListener.AcceptTcpClientAsync(token))
                        {
                            await using var data = client.GetStream();
                            if (encryptData)
                            {
                                await using var tls = new SslStream(data, leaveInnerStreamOpen: true);
                                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                                {
                                    ServerCertificate = _server.Certificate,
                                    EnabledSslProtocols = SslProtocols.Tls12,
                                }, token);
                                await TransferAsync(tls, line, command, token);
                            }
                            else
                            {
                                await TransferAsync(data, line, command, token);
                            }
                        }
                        dataListener.Stop();
                        dataListener = null;
                        await writer.WriteLineAsync("226 Transfer complete".AsMemory(), token);
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 Goodbye".AsMemory(), token);
                        return;
                    default:
                        await writer.WriteLineAsync("502 Unsupported command".AsMemory(), token);
                        break;
                }
            }
        }
        finally
        {
            dataListener?.Stop();
        }
    }

    private async Task TransferAsync(Stream data, string line, string command, CancellationToken token)
    {
        if (command == "NLST")
        {
            await data.WriteAsync(Encoding.UTF8.GetBytes(ExistingFileName + "\r\n"), token);
            await data.FlushAsync(token);
        }
        else
        {
            using var bytes = new MemoryStream();
            await data.CopyToAsync(bytes, token);
            UploadedBytes = bytes.ToArray();
            UploadedPath = line[5..];
        }
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();
}
