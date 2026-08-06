using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.Assemblies;
using OpenFarm.Domain.Assemblies;
using AddBomLineHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.Assemblies.AddBomLineCommand, OpenFarm.Domain.Assemblies.BomLine>;

namespace OpenFarm.Api.Endpoints.Assemblies;

public sealed class AddBomLineRequest : IActivityLoggable
{
    public Guid AssemblyId { get; set; }

    public string TargetKind { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    public decimal Quantity { get; set; }

    public string EntityType => "Assembly";

    public string EventType => "BomLineAdded";
}

public sealed class AddBomLineRequestValidator : AbstractValidator<AddBomLineRequest>
{
    public AddBomLineRequestValidator()
    {
        RuleFor(x => x.TargetKind).Must(k => Enum.TryParse<BomLineTargetKind>(k, out _))
            .WithMessage("targetKind must be one of Part, Assembly, NonPrintedComponent.");
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

/// <summary>POST /api/v1/assemblies/{id}/lines — adds one line (contracts/assembly-bom-api.md).</summary>
[Permission("assemblies:edit")]
public sealed class AddBomLineEndpoint(AddBomLineHandler handler, BomLineTargetNameResolver nameResolver)
    : Endpoint<AddBomLineRequest, BomLineResponse>
{
    public override void Configure()
    {
        Post("/api/v1/assemblies/{id}/lines");
        Policies("Permission");
    }

    public override async Task HandleAsync(AddBomLineRequest req, CancellationToken ct)
    {
        req.AssemblyId = Route<Guid>("id");
        var targetKind = Enum.Parse<BomLineTargetKind>(req.TargetKind);

        var line = await handler.HandleAsync(new AddBomLineCommand(req.AssemblyId, targetKind, req.TargetId, req.Quantity), ct);
        var targetName = await nameResolver.ResolveAsync(line, ct);

        Response = BomLineResponse.FromDomain(line, targetName);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
