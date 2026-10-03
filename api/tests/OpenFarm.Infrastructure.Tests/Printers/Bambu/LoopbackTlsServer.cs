using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>Test-only TLS listener with an ephemeral port, owned sessions, and bounded shutdown.</summary>
internal sealed class LoopbackTlsServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new(TimeSpan.FromSeconds(20));
    private readonly ConcurrentBag<TcpClient> _clients = [];
    private readonly ConcurrentBag<Task> _sessions = [];
    private readonly Func<SslStream, CancellationToken, Task> _handleSession;
    private readonly Task _acceptTask;

    public LoopbackTlsServer(Func<SslStream, CancellationToken, Task> handleSession)
    {
        _handleSession = handleSession;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=OpenFarm loopback tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptAsync();
    }

    public int Port { get; }
    public X509Certificate2 Certificate { get; }

    public bool TrustCertificate(X509Certificate? certificate) =>
        certificate is not null && certificate.GetCertHashString() == Certificate.GetCertHashString();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                client.NoDelay = true;
                _clients.Add(client);
                _sessions.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        await using (var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
        {
            try
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = Certificate,
                    EnabledSslProtocols = SslProtocols.Tls12,
                }, _stopping.Token);
                await _handleSession(stream, _stopping.Token);
            }
            catch (IOException)
            {
                // Tests deliberately close sockets and reject certificates.
            }
            catch (AuthenticationException)
            {
                // Expected when testing production rejection of this unrelated certificate.
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stopping.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        await _acceptTask.WaitAsync(TimeSpan.FromSeconds(3));
        _listener.Stop();
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        try
        {
            await Task.WhenAll(_sessions).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            Certificate.Dispose();
            _stopping.Dispose();
        }
    }
}
