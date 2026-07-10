using FastEndpoints;

namespace OpenFarm.Api.Endpoints.Ping;

public sealed class PingResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = "0.0.1-scaffold";
    public DateTimeOffset TimestampUtc { get; set; }
}

public sealed class PingEndpoint : EndpointWithoutRequest<PingResponse>
{
    public override void Configure()
    {
        Get("/api/v1/ping");
        AllowAnonymous();
    }

    public override Task HandleAsync(CancellationToken ct)
    {
        Response = new PingResponse { TimestampUtc = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }
}
