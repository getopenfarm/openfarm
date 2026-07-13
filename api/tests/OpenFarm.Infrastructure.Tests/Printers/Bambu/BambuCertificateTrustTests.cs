using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

/// <summary>
/// Regression coverage for the X509Chain root/intermediate partitioning bug: dumping every bundled
/// cert into CustomTrustStore (X509ChainTrustMode.CustomRootTrust requires self-signed roots only)
/// made every real printer's leaf cert unverifiable, since its issuing "Device CA" intermediate was
/// never reachable via ExtraStore.
/// </summary>
public class BambuCertificateTrustTests
{
    [Fact]
    public void LoadAndPartitionBundledCertificates_SplitsRealBundleIntoExpectedRootsAndIntermediates()
    {
        var (roots, intermediates) = BambuCertificateTrust.LoadAndPartitionBundledCertificates();

        // bambu.cert: BBL CA2 RSA, BBL CA2 ECC, legacy BBL CA (all self-signed).
        roots.Count.Should().Be(3);

        // bambu.cert's 2 cross-sign bridge certs (BBL CA2 RSA/ECC issued by the legacy BBL CA) plus
        // the 3 per-model Device CA certs (bambu_h2c/p2s/x2c, each issued by BBL CA2 RSA).
        intermediates.Count.Should().Be(5);

        foreach (X509Certificate2 root in roots)
        {
            root.SubjectName.RawData.Should().Equal(root.IssuerName.RawData);
        }

        foreach (X509Certificate2 intermediate in intermediates)
        {
            intermediate.SubjectName.RawData.Should().NotEqual(intermediate.IssuerName.RawData);
        }
    }

    [Fact]
    public void Validate_WithUnrelatedSelfSignedCertificate_ReturnsFalse()
    {
        using var unrelated = CreateSelfSignedCertificate();

        BambuCertificateTrust.Validate(unrelated).Should().BeFalse();
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=not-bambu", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }
}
