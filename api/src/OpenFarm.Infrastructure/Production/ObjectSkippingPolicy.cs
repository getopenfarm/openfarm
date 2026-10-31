using Microsoft.Extensions.Configuration;
using OpenFarm.Application.Printers;

namespace OpenFarm.Infrastructure.Production;

/// <summary>Empty by default. Entries attest hardware validation on this printer's installed firmware.</summary>
public sealed class ObjectSkippingPolicy(IConfiguration configuration) : OpenFarm.Application.Production.IObjectSkippingPolicy
{
    public void EnsureVerified(PrinterConnectionDetails details, bool live)
    {
        var operation = live ? "Live" : "Start";
        var verified = configuration.GetSection($"ObjectSkipping:Verified{operation}Printers").Get<string[]>() ?? [];
        if (!verified.Contains(details.SerialNumber, StringComparer.Ordinal))
            throw new OpenFarm.Application.Production.InvalidProductionDemandException("Object skipping has not been verified on this printer’s installed firmware. Use a verified printer or complete hardware validation first.");
    }
}
