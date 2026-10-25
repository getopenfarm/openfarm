using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Tests.Persistence;

public class DatabaseConnectionStringTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void Resolve_BuildsTheStringFromTheDatabaseSection()
    {
        var connectionString = DatabaseConnectionString.Resolve(Config(
            ("Database:Host", "db.local"),
            ("Database:Port", "6543"),
            ("Database:Name", "farm"),
            ("Database:Username", "operator"),
            ("Database:Password", "s3cret;pass")));

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Host.Should().Be("db.local");
        parsed.Port.Should().Be(6543);
        parsed.Database.Should().Be("farm");
        parsed.Username.Should().Be("operator");
        parsed.Password.Should().Be("s3cret;pass");
    }

    [Fact]
    public void Resolve_PrefersAConfiguredConnectionString()
    {
        var connectionString = DatabaseConnectionString.Resolve(Config(
            ("ConnectionStrings:Default", "Host=explicit;Database=other"),
            ("POSTGRES_USER", "ignored"),
            ("Database:Host", "ignored"),
            ("Database:Port", "5432")));

        connectionString.Should().Be("Host=explicit;Database=other");
    }

    [Fact]
    public void Resolve_PostgresVariablesBeatTheDatabaseSection_AndTheDatabaseDefaultsToTheUser()
    {
        var connectionString = DatabaseConnectionString.Resolve(Config(
            ("POSTGRES_USER", "farmer"),
            ("POSTGRES_PASSWORD", "from-env"),
            ("Database:Host", "db.local"),
            ("Database:Port", "5432"),
            ("Database:Name", "openfarm"),
            ("Database:Username", "openfarm"),
            ("Database:Password", "openfarm")));

        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Username.Should().Be("farmer");
        parsed.Password.Should().Be("from-env");
        parsed.Database.Should().Be("farmer");
    }

    [Fact]
    public void Resolve_PostgresDbBeatsTheUserAsTheDatabaseName()
    {
        var connectionString = DatabaseConnectionString.Resolve(Config(
            ("POSTGRES_USER", "farmer"),
            ("POSTGRES_DB", "farm"),
            ("Database:Host", "db.local"),
            ("Database:Port", "5432")));

        new NpgsqlConnectionStringBuilder(connectionString).Database.Should().Be("farm");
    }
}
