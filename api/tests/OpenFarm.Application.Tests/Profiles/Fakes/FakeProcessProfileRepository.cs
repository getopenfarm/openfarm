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

    public Task<IReadOnlyList<ProcessProfile>> ListAsync(CancellationToken cancellationToken, ProfileCatalogScope? scope = null) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            scope is null ? _rows.ToList() : _rows.Where(p => scope.Includes(p.Origin, p.SourceFormat, p.SourceVersion)).ToList());

    public Task<IReadOnlyList<ProcessProfile>> FindByNameInCatalogAsync(
        ProfileSourceFormat sourceFormat, string? sourceVersion, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name
                && (p.Origin == ProfileOrigin.User || p.SourceVersion == sourceVersion)).ToList());

    public Task<IReadOnlyList<ProcessProfile>> FindByNameAnyVersionAsync(
        ProfileSourceFormat sourceFormat, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Name == name).ToList());

    public Task<IReadOnlyList<ProcessProfile>> FindInCatalogByIdentityAsync(
        ProfileSourceFormat sourceFormat, string sourceVersion, string? externalId, string? vendorName, string name, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion
                && ((externalId != null && p.ExternalId == externalId) || (p.VendorName == vendorName && p.Name == name))).ToList());

    public Task<IReadOnlyList<ProcessProfile>> ListSystemCatalogAsync(ProfileSourceFormat sourceFormat, string? sourceVersion, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProcessProfile>>(
            _rows.Where(p => p.SourceFormat == sourceFormat && p.Origin == ProfileOrigin.System && p.SourceVersion == sourceVersion).ToList());

    public void Stage(ProcessProfile profile) => _rows.Add(profile);

    public Task<(IReadOnlyList<ProcessProfile> Items, int TotalCount)> SearchAsync(
            ProfileOrigin? origin,
            bool? isSelectable,
            string? compatibleWithMachineName,
            string? materialType,
            string? searchText,
            int page,
            int pageSize,
            CancellationToken cancellationToken, ProfileCatalogScope? catalog = null)
    {
        var candidates = _rows.AsEnumerable();

        if (catalog is not null)
        {
            candidates = candidates.Where(p => catalog.Includes(p.Origin, p.SourceFormat, p.SourceVersion));
        }

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
