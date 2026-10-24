using OpenFarm.Application.Common;

namespace OpenFarm.Application.Printers;

/// <summary>A printer heard announcing itself on the LAN. <c>ModelCode</c> is null when the
/// vendor's device code has no catalog mapping (<c>RawModel</c> keeps the original).</summary>
public sealed record DiscoveredPrinter(
    string SerialNumber,
    string Name,
    string Brand,
    string? ModelCode,
    string RawModel,
    string IpAddress,
    DateTimeOffset LastSeenAt);

/// <summary>
/// LAN printer discovery, one implementation per vendor protocol. It reports whatever the host can hear; a future
/// local-agent relay (Phase 23) can replace the direct-LAN implementations behind this contract.
/// </summary>
public interface IPrinterDiscovery
{
    IReadOnlyList<DiscoveredPrinter> GetRecent();
}

public sealed record GetDiscoveredPrintersQuery;

public sealed record DiscoveredPrinterResult(DiscoveredPrinter Printer, bool AlreadyRegistered);

public sealed class GetDiscoveredPrintersQueryHandler(IEnumerable<IPrinterDiscovery> discoveries, IPrinterRepository printers)
    : IQueryHandler<GetDiscoveredPrintersQuery, IReadOnlyList<DiscoveredPrinterResult>>
{
    public async Task<IReadOnlyList<DiscoveredPrinterResult>> HandleAsync(GetDiscoveredPrintersQuery query, CancellationToken cancellationToken)
    {
        var registered = (await printers.ListAsync(cancellationToken))
            .Select(p => p.SerialNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return discoveries.SelectMany(d => d.GetRecent())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new DiscoveredPrinterResult(p, registered.Contains(p.SerialNumber)))
            .ToList();
    }
}
