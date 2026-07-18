using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using CreateFilamentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CreateFilamentCommand, OpenFarm.Domain.FilamentTracking.Filament>;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class CreateFilamentVariantRequest
{
    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;
}

public sealed class CreateFilamentRequest : IActivityLoggable
{
    public Guid VendorId { get; set; }

    public string Material { get; set; } = string.Empty;

    public string MaterialType { get; set; } = string.Empty;

    public Guid? FilamentProfileId { get; set; }

    public IReadOnlyList<CreateFilamentVariantRequest> Variants { get; set; } = [];

    public string EntityType => "Filament";

    public string EventType => "FilamentCreated";
}

public sealed class CreateFilamentRequestValidator : AbstractValidator<CreateFilamentRequest>
{
    public CreateFilamentRequestValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty();
        RuleFor(x => x.Material).NotEmpty();
        RuleFor(x => x.MaterialType).NotEmpty();
        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.ColorName).NotEmpty();
            variant.RuleFor(v => v.ColorHex).NotEmpty();
        });
    }
}

/// <summary>Creates a Filament catalog entry with at least one color variant (FR-016). 404 on an unresolvable VendorId.</summary>
[Permission("filaments:create")]
public sealed class CreateFilamentEndpoint(CreateFilamentHandler handler, IVendorRepository vendorRepository)
    : Endpoint<CreateFilamentRequest, FilamentResponse>
{
    public override void Configure()
    {
        Post("/api/v1/filaments");
        Policies("Permission");
    }

    public override async Task HandleAsync(CreateFilamentRequest req, CancellationToken ct)
    {
        var command = new CreateFilamentCommand(
            req.VendorId,
            req.Material,
            req.MaterialType,
            req.FilamentProfileId,
            req.Variants.Select(v => new CreateFilamentVariantInput(v.ColorName, v.ColorHex)).ToList());

        var filament = await handler.HandleAsync(command, ct);
        var vendor = await vendorRepository.GetByIdAsync(filament.VendorId, ct);

        Response = FilamentResponse.FromDomain(filament, vendor?.Name ?? string.Empty);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
