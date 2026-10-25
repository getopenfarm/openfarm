using Microsoft.Extensions.Configuration;
using Npgsql;

namespace OpenFarm.Infrastructure.Persistence;

public static class DatabaseConnectionString
{
    // A configured ConnectionStrings:Default wins. Otherwise the string is built from the Postgres image's own
    // variables (POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB), so one .env configures the server and the app,
    // falling back to the Database section. As in the image, the database defaults to the user.
    public static string Resolve(IConfiguration configuration) =>
        configuration.GetConnectionString("Default") is { Length: > 0 } configured
            ? configured
            : new NpgsqlConnectionStringBuilder
            {
                Host = configuration["Database:Host"],
                Port = configuration.GetValue<int>("Database:Port"),
                Database = configuration["POSTGRES_DB"] ?? configuration["POSTGRES_USER"] ?? configuration["Database:Name"],
                Username = configuration["POSTGRES_USER"] ?? configuration["Database:Username"],
                Password = configuration["POSTGRES_PASSWORD"] ?? configuration["Database:Password"],
            }.ConnectionString;
}
