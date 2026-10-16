using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using CreateNonPrintedComponentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.CreateNonPrintedComponentCommand, OpenFarm.Domain.Assemblies.NonPrintedComponent>;

namespace OpenFarm.Api.Endpoints.NonPrintedComponents;

public sealed class CreateNonPrintedComponentRequest : IActivityLoggable
{
    public string Name { get; set; } = string.Empty;

    public decimal? UnitCost { get; set; }

    public string EntityType => "NonPrintedComponent";

    public string EventType => "NonPrintedComponentCreated";
}

public sealed class CreateNonPrintedComponentRequestValidator : AbstractValidator<CreateNonPrintedComponentRequest>
{
    public CreateNonPrintedComponentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

/// <summary>POST /api/v1/non-printed-components (contracts/assembly-bom-api.md).</summary>
[Permission("non-printed-components:create")]
public sealed class CreateNonPrintedComponentEndpoint(CreateNonPrintedComponentHandler handler)
    : Endpoint<CreateNonPrintedComponentRequest, NonPrintedComponentResponse>
{
    public override void Configure()
    {
        Post("/api/v1/non-printed-components");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateNonPrintedComponentRequest req, CancellationToken ct)
    {
        var component = await handler.HandleAsync(new CreateNonPrintedComponentCommand(req.Name, req.UnitCost), ct);

        Response = NonPrintedComponentResponse.FromDomain(component);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
