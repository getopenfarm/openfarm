using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using EditBomLineHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.UpdateBomLineQuantityCommand, OpenFarm.Domain.Assemblies.BomLine>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class EditBomLineRequest : IActivityLoggable
{
    public Guid AssemblyId { get; set; }

    public Guid LineId { get; set; }

    public decimal Quantity { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "BomLineQuantityChanged";
}

public sealed class EditBomLineRequestValidator : AbstractValidator<EditBomLineRequest>
{
    public EditBomLineRequestValidator() => RuleFor(x => x.Quantity).GreaterThan(0);
}

/// <summary>PUT /api/v1/assemblies/{id}/lines/{lineId} — quantity-only; target is immutable once created (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:edit")]
public sealed class EditBomLineEndpoint(EditBomLineHandler handler, BomLineTargetNameResolver nameResolver)
    : Endpoint<EditBomLineRequest, BomLineResponse>
{
    public override void Configure()
    {
        Put("/api/v1/assemblies/{id}/lines/{lineId}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditBomLineRequest req, CancellationToken ct)
    {
        req.AssemblyId = Route<Guid>("id");
        req.LineId = Route<Guid>("lineId");

        var line = await handler.HandleAsync(new UpdateBomLineQuantityCommand(req.AssemblyId, req.LineId, req.Quantity), ct);
        var targetName = await nameResolver.ResolveAsync(line, ct);

        Response = BomLineResponse.FromDomain(line, targetName);
    }
}
