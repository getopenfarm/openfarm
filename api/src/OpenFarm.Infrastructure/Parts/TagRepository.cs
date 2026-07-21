using Microsoft.EntityFrameworkCore;
using OpenFarm.Application.Parts;
using OpenFarm.Domain.Parts;
using OpenFarm.Infrastructure.Persistence;

namespace OpenFarm.Infrastructure.Parts;

public sealed class TagRepository(OpenFarmDbContext dbContext) : ITagRepository
{
    public async Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Tags.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tag?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        dbContext.Tags.FirstOrDefaultAsync(t => t.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Tags.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
