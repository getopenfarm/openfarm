using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeProcessProfileRepository : IProcessProfileRepository
{
    private readonly List<ProcessProfile> _rows = [];

    public IReadOnlyList<ProcessProfile> Rows => _rows;

    public Task AddAsync(ProcessProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<ProcessProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(_rows.ToList());

    public Task<IReadOnlyList<ProcessProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<ProcessProfile?> FindByExternalIdAsync(string vendorName, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId));

    public Task<ProcessProfile?> FindSystemByNameAsync(string vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

    public Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
        ProfileOrigin? origin,
        bool? isSelectable,
        string? compatibleWithMachineName,
        string? materialType,
        string? searchText,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var candidates = _rows.AsEnumerable();

        if (origin is { } requestedOrigin)
        {
            candidates = candidates.Where(p => p.Origin == requestedOrigin);
        }

        candidates = isSelectable switch
        {
            true => candidates.Where(p => (p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User),
            false => candidates.Where(p => !((p.IsSelectable && p.IsCurrentlyOffered) || p.Origin == ProfileOrigin.User)),
            null => candidates,
        };

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            candidates = candidates.Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(compatibleWithMachineName))
        {
            candidates = candidates.Where(p =>
                p.CompatibleMachineNames is not null && p.CompatibleMachineNames.Contains(compatibleWithMachineName, StringComparer.Ordinal));
        }

        var ordered = candidates.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var totalCount = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<ProcessProfile> Items, int TotalCount)>((items, totalCount));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
