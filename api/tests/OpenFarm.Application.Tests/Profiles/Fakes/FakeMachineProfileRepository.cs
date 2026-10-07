using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeMachineProfileRepository : IMachineProfileRepository
{
    private readonly List<MachineProfile> _rows = [];

    public IReadOnlyList<MachineProfile> Rows => _rows;

    public Task AddAsync(MachineProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<MachineProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<MachineProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(_rows.ToList());

    public Task<IReadOnlyList<MachineProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MachineProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<MachineProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId));

    public Task<MachineProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

    public Task<(IReadOnlyList<MachineProfile> Items, int TotalCount)> SearchAsync(
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

        var ordered = candidates.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var totalCount = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<MachineProfile> Items, int TotalCount)>((items, totalCount));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
