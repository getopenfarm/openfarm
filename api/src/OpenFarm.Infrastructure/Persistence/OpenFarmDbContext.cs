using Microsoft.EntityFrameworkCore;

namespace OpenFarm.Infrastructure.Persistence;

public sealed class OpenFarmDbContext(DbContextOptions<OpenFarmDbContext> options) : DbContext(options);
