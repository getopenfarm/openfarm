using FluentAssertions;
using FluentFTP.Exceptions;
using OpenFarm.Infrastructure.Printers.Bambu;

namespace OpenFarm.Infrastructure.Tests.Printers.Bambu;

public class BambuFileMissingTests
{
    [Fact]
    public void A550Reply_MeansTheFileIsNotThere()
    {
        var download = new FtpException("Error while downloading the file from the server.", new FtpCommandException("550", "Not found"));

        BambuLanConnector.IsFileMissing(download).Should().BeTrue();
    }

    [Fact]
    public void AnyOtherFailure_IsNotMistakenForAMissingFile()
    {
        BambuLanConnector.IsFileMissing(new FtpException("x", new FtpCommandException("426", "Connection closed"))).Should().BeFalse();
        BambuLanConnector.IsFileMissing(new FtpException("x", new IOException("reset"))).Should().BeFalse();
        BambuLanConnector.IsFileMissing(new FtpException("x")).Should().BeFalse();
    }
}
