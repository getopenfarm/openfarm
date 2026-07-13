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

        var (roots, intermediates) = BundledTrustedCertificates.Value;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        // X509ChainTrustMode.CustomRootTrust requires CustomTrustStore to contain only self-signed
        // roots — Bambu's cross-sign bridge certs and per-model "Device CA" intermediates go here
        // instead, or X509Chain.Build can never locate a real printer leaf cert's issuer (a
        // .NET-specific distinction pybambu's Python ssl.SSLContext doesn't need to make, since
        // load_verify_locations() treats every loaded cert as usable for both roles).
        chain.ChainPolicy.ExtraStore.AddRange(intermediates);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        // Bambu's certs may lack a Key Usage extension — pybambu clears VERIFY_X509_STRICT for the
        // same reason (research.md §1); hostname is intentionally never checked (printers are
        // addressed by LAN IP, matching pybambu's check_hostname = False).
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreInvalidBasicConstraints;

        return chain.Build(serverCertificate);
    }

    private static readonly Lazy<(X509Certificate2Collection Roots, X509Certificate2Collection Intermediates)> BundledTrustedCertificates =
        new(LoadAndPartitionBundledCertificates);

    /// <summary>
    /// Internal (not private) so <c>InternalsVisibleTo</c> lets Infrastructure.Tests assert the
    /// bundled cert set partitions as expected — this exact partitioning is what the TLS handshake
    /// bug above was in.
    /// </summary>
    internal static (X509Certificate2Collection Roots, X509Certificate2Collection Intermediates) LoadAndPartitionBundledCertificates()
    {
        var all = new X509Certificate2Collection();
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resourceName in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".cert", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            // Some bundles (e.g. bambu.cert) concatenate multiple PEM certificates in one resource —
            // ImportFromPem (unlike X509CertificateLoader.LoadCertificate) imports every cert block
            // found in the text, not just the first.
            all.ImportFromPem(reader.ReadToEnd());
        }

        var roots = new X509Certificate2Collection();
        var intermediates = new X509Certificate2Collection();
        foreach (X509Certificate2 cert in all)
        {
            var isSelfSigned = cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData);
            (isSelfSigned ? roots : intermediates).Add(cert);
        }

        return (roots, intermediates);
    }
}
