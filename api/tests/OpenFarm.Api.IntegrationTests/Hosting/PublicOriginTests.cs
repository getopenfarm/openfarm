using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OpenFarm.Api.Hosting;

namespace OpenFarm.Api.IntegrationTests.Hosting;

public class PublicOriginTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("  ", null, false)]
    [InlineData("https://Farm.Example:443/", "https://farm.example", true)]
    [InlineData("http://localhost:80", "http://localhost", false)]
    [InlineData("http://192.168.1.2:6868/", "http://192.168.1.2:6868", false)]
    [InlineData("https://[::1]:8443/", "https://[::1]:8443", true)]
    public void Configuration_NormalizesOrigins(string? value, string? expected, bool isHttps)
    {
        var origin = Create(value);
        origin.Value.Should().Be(expected);
        origin.IsHttps.Should().Be(isHttps);
    }

    [Theory]
    [InlineData("farm.example")]
    [InlineData("//farm.example")]
    [InlineData("ftp://farm.example")]
    [InlineData("https://user:password@farm.example")]
    [InlineData("https://@farm.example")]
    [InlineData("https://farm.example/app")]
    [InlineData("https://farm.example/app/..")]
    [InlineData("https://farm.example//")]
    [InlineData("https://farm.example?query=1")]
    [InlineData("https://farm.example?")]
    [InlineData("https://farm.example#fragment")]
    [InlineData("https://farm.example#")]
    [InlineData(" https://farm.example")]
    [InlineData("https://farm.example\\app")]
    public void Configuration_RejectsInvalidOrigins(string value)
    {
        var act = () => Create(value);
        act.Should().Throw<InvalidOperationException>().WithMessage("ORIGIN*");
    }

    private static PublicOrigin Create(string? value) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ORIGIN"] = value }).Build());
}
