using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Processors;
using OpenFarm.Api.Security;
using OpenFarm.Application.FilamentTracking;
using EditFilamentHandler = OpenFarm.Application.Common.ICommandHandler<OpenFarm.Application.FilamentTracking.EditFilamentCommand, OpenFarm.Domain.FilamentTracking.Filament>;

namespace OpenFarm.Api.Endpoints.Filaments;

public sealed class EditFilamentRequest : IActivityLoggable
{
    private decimal? _pricePerKg;
    public decimal? PricePerKg
    {
        get => _pricePerKg;
        set { _pricePerKg = value; HasPricePerKg = true; }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasPricePerKg { get; private set; }

    public IReadOnlyList<FilamentVariantPriceInput> VariantPrices { get; set; } = [];

    public Guid Id { get; set; }

    public Guid VendorId { get; set; }

    public string Material { get; set; } = string.Empty;

    public string MaterialType { get; set; } = string.Empty;

    public IReadOnlyList<CreateFilamentVariantRequest> NewVariants { get; set; } = [];

    public IReadOnlyList<FilamentProfileAssignmentInput> ProfileAssignments { get; set; } = [];

    public string EntityType => "Filament";

    public string EventType => "FilamentEdited";
}

public sealed class EditFilamentRequestValidator : AbstractValidator<EditFilamentRequest>
{
    public EditFilamentRequestValidator()
    {
        RuleFor(x => x.VendorId).NotEmpty();
        RuleFor(x => x.Material).NotEmpty();
        RuleFor(x => x.PricePerKg).GreaterThanOrEqualTo(0);
        RuleFor(x => x.VariantPrices).NotNull();
        RuleFor(x => x.VariantPrices).Must(p => p.Select(v => v.VariantId).Distinct().Count() == p.Count)
            .When(x => x.VariantPrices is not null);
        RuleForEach(x => x.VariantPrices).ChildRules(price =>
        {
            price.RuleFor(p => p.VariantId).NotEmpty();
            price.RuleFor(p => p.PricePerKg).GreaterThanOrEqualTo(0);
        });
        RuleFor(x => x.MaterialType).NotEmpty();
        RuleFor(x => x.NewVariants).NotNull();
        RuleFor(x => x.ProfileAssignments).NotNull();
        RuleForEach(x => x.NewVariants).ChildRules(variant =>
        {
            variant.RuleFor(v => v.ColorName).NotEmpty();
            variant.RuleFor(v => v.ColorHex).NotEmpty();
            variant.RuleFor(v => v.PricePerKg).GreaterThanOrEqualTo(0);
        });
    }
}

/// <summary>PUT /api/v1/filaments/{id} — updates vendor/material/type, adds colour variants and replaces profile assignments in one all-or-nothing save. Existing variant identity and color stay immutable; prices can be updated.</summary>
[Permission("filaments:assign-profiles")]
public sealed class EditFilamentEndpoint(EditFilamentHandler handler, IVendorRepository vendorRepository)
    : Endpoint<EditFilamentRequest, FilamentResponse>
{
    public override void Configure()
    {
        Put("/api/v1/filaments/{id}");
        Policies("Permission");
    }

    public override async Task HandleAsync(EditFilamentRequest req, CancellationToken ct)
    {
        req.Id = Route<Guid>("id");
        var command = new EditFilamentCommand(
            req.Id,
            req.VendorId,
            req.Material,
            req.MaterialType,
            req.NewVariants.Select(v => new CreateFilamentVariantInput(v.ColorName, v.ColorHex, v.PricePerKg)).ToList(),
            req.ProfileAssignments, req.PricePerKg, req.HasPricePerKg, req.VariantPrices);

        var filament = await handler.HandleAsync(command, ct);
        var vendor = await vendorRepository.GetByIdAsync(filament.VendorId, ct);

        Response = FilamentResponse.FromDomain(filament, vendor?.Name ?? string.Empty);
    }
}
