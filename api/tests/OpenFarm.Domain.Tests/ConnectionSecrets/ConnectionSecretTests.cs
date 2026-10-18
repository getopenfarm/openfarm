using FluentAssertions;
using OpenFarm.Domain.ConnectionSecrets;

namespace OpenFarm.Domain.Tests.ConnectionSecrets;

public class ConnectionSecretTests
{
    [Fact]
    public void Create_WithValidArguments_SetsAllFields()
    {
        var beforeCreate = DateTimeOffset.UtcNow;

        var secret = ConnectionSecret.Create("AC12345", "ciphertext-value");

        secret.Id.Should().NotBeEmpty();
        secret.PrinterSerialNumber.Should().Be("AC12345");
        secret.EncryptedAccessCode.Should().Be("ciphertext-value");
        secret.CreatedAtUtc.Should().BeOnOrAfter(beforeCreate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyPrinterSerialNumber_Throws(string? serialNumber)
    {
        var act = () => ConnectionSecret.Create(serialNumber!, "ciphertext-value");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_WithEmptyEncryptedAccessCode_Throws(string? encryptedAccessCode)
    {
        var act = () => ConnectionSecret.Create("AC12345", encryptedAccessCode!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConnectionSecret_HasNoSettersOrUpdateMethod()
    {
        var type = typeof(ConnectionSecret);

        type.GetProperties()
            .Should().OnlyContain(p => p.SetMethod == null || !p.SetMethod.IsPublic);

        type.GetMethods()
            .Should().NotContain(m => m.Name.StartsWith("Update", StringComparison.Ordinal));
    }
}
