using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.ConnectionSecrets;
using OpenFarm.Domain.ConnectionSecrets;
using CreateConnectionSecretHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.ConnectionSecrets.CreateConnectionSecretCommand, OpenFarm.Domain.ConnectionSecrets.ConnectionSecret>;

namespace OpenFarm.Api.Endpoints.ConnectionSecrets;

public sealed class CreateConnectionSecretRequest : IActivityLoggable
{
    public string PrinterSerialNumber { get; set; } = string.Empty;

    public string PlainAccessCode { get; set; } = string.Empty;

    public string EntityType => "ConnectionSecret";

    public string EventType => "Created";
}

public sealed class CreateConnectionSecretRequestValidator : AbstractValidator<CreateConnectionSecretRequest>
{
    public CreateConnectionSecretRequestValidator()
    {
        RuleFor(x => x.PrinterSerialNumber).NotEmpty();
        RuleFor(x => x.PlainAccessCode).NotEmpty();
    }
}

public sealed class ConnectionSecretResponse
{
    public Guid Id { get; set; }

    public string PrinterSerialNumber { get; set; } = string.Empty;

    public string MaskedAccessCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}

[Permission("connection-secrets:create")]
public sealed class CreateConnectionSecretEndpoint(CreateConnectionSecretHandler handler)
    : Endpoint<CreateConnectionSecretRequest, ConnectionSecretResponse>
{
    public override void Configure()
    {
        Post("/api/v1/connection-secrets");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateConnectionSecretRequest req, CancellationToken ct)
    {
        var result = await handler.HandleAsync(
            new CreateConnectionSecretCommand(req.PrinterSerialNumber, req.PlainAccessCode),
            ct);

        Response = new ConnectionSecretResponse
        {
            Id = result.Id,
            PrinterSerialNumber = result.PrinterSerialNumber,
            MaskedAccessCode = "***",
            CreatedAtUtc = result.CreatedAtUtc,
        };
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
