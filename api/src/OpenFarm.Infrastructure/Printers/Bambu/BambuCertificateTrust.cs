using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace OpenFarm.Infrastructure.Printers.Bambu;

/// <summary>
/// Shared TLS trust logic for both Bambu transports (MQTT/MQTTnet and the camera relay/SslStream) —
/// pins the bundled per-device cert set (vendored from pybambu/certs/) as a custom trust root rather
/// than a public CA, mirroring pybambu's <c>create_local_ssl_context()</c> (research.md §1). Never a
/// blanket accept-all.
/// </summary>
internal static class BambuCertificateTrust
{
    public static bool Validate(X509Certificate? certificate)
    {
        if (certificate is not X509Certificate2 serverCertificate)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(BundledTrustedCertificates.Value);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        // Bambu's certs may lack a Key Usage extension — pybambu clears VERIFY_X509_STRICT for the
        // same reason (research.md §1); hostname is intentionally never checked (printers are
        // addressed by LAN IP, matching pybambu's check_hostname = False).
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreInvalidBasicConstraints;

        return chain.Build(serverCertificate);
    }

    private static readonly Lazy<X509Certificate2Collection> BundledTrustedCertificates = new(LoadBundledCertificates);

    private static X509Certificate2Collection LoadBundledCertificates()
    {
        var collection = new X509Certificate2Collection();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resourceName in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cert", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            collection.Add(X509CertificateLoader.LoadCertificate(memory.ToArray()));
        }

        return collection;
    }
}
