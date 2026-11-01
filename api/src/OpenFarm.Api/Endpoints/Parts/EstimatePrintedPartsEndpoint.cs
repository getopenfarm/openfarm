using FastEndpoints;
using FluentValidation;
using OpenFarm.Api.Security;
using OpenFarm.Application.Parts;

namespace OpenFarm.Api.Endpoints.Parts;

public sealed record PrintedPartEstimateInput(Guid PartId, decimal Quantity);
public sealed class EstimatePrintedPartsRequest
{
    public List<PrintedPartEstimateInput> Parts { get; set; } = [];
}
public sealed class EstimatePrintedPartsValidator : Validator<EstimatePrintedPartsRequest>
{
    public EstimatePrintedPartsValidator()
    {
        RuleFor(r => r.Parts).NotNull();
        RuleForEach(r => r.Parts).ChildRules(part =>
        {
            part.RuleFor(p => p.PartId).NotEmpty();
            part.RuleFor(p => p.Quantity).InclusiveBetween(0, int.MaxValue);
        });
    }
}

/// <summary>Read-only estimates for a live draft; does not create demand or require printable/sliceable parts.</summary>
[Permission("parts:list")]
public sealed class EstimatePrintedPartsEndpoint(IPartRepository parts) : Endpoint<EstimatePrintedPartsRequest, PrintEstimate>
{
    public override void Configure() { Post("/api/v1/parts/print-estimate"); Policies("Permission"); }
    public override async Task HandleAsync(EstimatePrintedPartsRequest req, CancellationToken ct)
    {
        var inputs = req.Parts.GroupBy(p => p.PartId).OrderBy(g => g.Key).ToArray();
        var catalog = (await parts.GetForEstimatesAsync(inputs.Select(p => p.Key).ToArray(), ct)).ToDictionary(p => p.Id);
        var estimates = inputs.Select(input => PrintEstimates.ForPart(catalog.GetValueOrDefault(input.Key), input.Sum(p => p.Quantity)));
        Response = PrintEstimates.Sum(estimates);
    }
}
