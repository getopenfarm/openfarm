using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using EditNonPrintedComponentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.EditNonPrintedComponentCommand, OpenFarm.Domain.Assemblies.NonPrintedComponent>;

namespace OpenFarm.Api.Endpoints.NonPrintedComponents;

public sealed class EditNonPrintedComponentRequest : IActivityLoggable
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal? UnitCost { get; set; }

    public string EntityType => "NonPrintedComponent";

    public string EventType => "NonPrintedComponentEdited";
}

public sealed class EditNonPrintedComponentRequestValidator : AbstractValidator<EditNonPrintedComponentRequest>
{
    public EditNonPrintedComponentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty();
    }
}

/// <summary>PUT /api/v1/non-printed-components/{id} (contracts/assembly-bom-api.md).</summary>
[Permission("non-printed-components:edit")]
public sealed class EditNonPrintedComponentEndpoint(EditNonPrintedComponentHandler handler)
    : Endpoint<EditNonPrintedComponentRequest, NonPrintedComponentResponse>
{
    public override void Configure()
    {
        Put("/api/v1/non-printed-components/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditNonPrintedComponentRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var component = await handler.HandleAsync(new EditNonPrintedComponentCommand(req.Id, req.Name, req.UnitCost), ct);

        Response = NonPrintedComponentResponse.FromDomain(component);
    }
}
