using OpenFarm.Application.Profiles;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles.Fakes;

public sealed class FakeFilamentProfileRepository : IFilamentProfileRepository
{
    private readonly List<FilamentProfile> _rows = [];

    public IReadOnlyList<FilamentProfile> Rows => _rows;

    public Task AddAsync(FilamentProfile profile, CancellationToken cancellationToken)
    {
        _rows.Add(profile);
        return Task.CompletedTask;
    }

    public Task<FilamentProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<FilamentProfile>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FilamentProfile>>(_rows.ToList());

    public Task<IReadOnlyList<FilamentProfile>> FindByNameAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FilamentProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<FilamentProfile?> FindByExternalIdAsync(ProfileSourceFormat sourceFormat, string vendorName, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == externalId));

    public Task<FilamentProfile?> FindSystemByNameAsync(ProfileSourceFormat sourceFormat, string vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult(_rows.FirstOrDefault(p =>
            p.Origin == ProfileOrigin.System && p.VendorName == vendorName && p.ExternalId == null && p.Name == name));

    public Task<(IReadOnlyList<FilamentProfile> Items, int TotalCount)> SearchAsync(
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

        if (!string.IsNullOrWhiteSpace(materialType))
        {
            candidates = candidates.Where(p => p.MaterialType == materialType);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            candidates = candidates.Where(p => p.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(compatibleWithMachineName))
        {
            candidates = candidates.Where(p =>
                p.CompatibleMachineNames is null || p.CompatibleMachineNames.Count == 0
                || p.CompatibleMachineNames.Contains(compatibleWithMachineName, StringComparer.Ordinal));
        }

        var ordered = candidates.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        var totalCount = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<FilamentProfile> Items, int TotalCount)>((items, totalCount));
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
