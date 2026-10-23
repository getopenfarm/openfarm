using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using OpenFarm.Domain.FilamentTracking;
using CreateFilamentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.CreateFilamentCommand, OpenFarm.Domain.FilamentTracking.Filament>;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class CreateFilamentVariantRequest
{
    public decimal? PricePerKg { get; set; }

    /// <summary>Grams on hand below which this colour shows as low; null falls back to the filament's default.</summary>
    public decimal? LowStockGrams { get; set; }

    /// <summary>Days left below which this colour shows as low, 0–365; null falls back to the filament's default.</summary>
    public int? LowStockDays { get; set; }

    public string ColorName { get; set; } = string.Empty;

    public string ColorHex { get; set; } = string.Empty;
}

public sealed class CreateFilamentRequest : IActivityLoggable
{
    public decimal? PricePerKg { get; set; }

    /// <summary>Default low-stock threshold, in grams on hand, for colours that have been stocked; null for no alert.</summary>
    public decimal? LowStockGrams { get; set; }

    /// <summary>Default low-stock threshold, in days left at the last 30 days' pace, 0–365; null for no alert by days.</summary>
    public int? LowStockDays { get; set; }

    public Guid VendorId { get; set; }

    public string Material { get; set; } = string.Empty;

    public string MaterialType { get; set; } = string.Empty;

    public IReadOnlyList<FilamentProfileAssignmentInput> ProfileAssignments { get; set; } = [];

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
        RuleFor(x => x.PricePerKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockGrams).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LowStockDays).InclusiveBetween(0, Filament.MaxLowStockDays).When(x => x.LowStockDays is not null);
        RuleFor(x => x.MaterialType).NotEmpty();
        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.ColorName).NotEmpty();
            variant.RuleFor(v => v.ColorHex).NotEmpty();
            variant.RuleFor(v => v.PricePerKg).GreaterThanOrEqualTo(0);
            variant.RuleFor(v => v.LowStockGrams).GreaterThanOrEqualTo(0);
            variant.RuleFor(v => v.LowStockDays).InclusiveBetween(0, Filament.MaxLowStockDays).When(v => v.LowStockDays is not null);
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
            req.Variants.Select(v => new CreateFilamentVariantInput(v.ColorName, v.ColorHex, v.PricePerKg, v.LowStockGrams, v.LowStockDays)).ToList(),
            req.ProfileAssignments, req.PricePerKg, req.LowStockGrams, req.LowStockDays);

        var filament = await handler.HandleAsync(command, ct);
        var vendor = await vendorRepository.GetByIdAsync(filament.VendorId, ct);

        Response = FilamentResponse.FromDomain(filament, vendor?.Name ?? string.Empty);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
    }
}
