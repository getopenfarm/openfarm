using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Library;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class LibraryFolderConfiguration : IEntityTypeConfiguration<LibraryFolder>
{
    public void Configure(EntityTypeBuilder<LibraryFolder> builder)
    {
        builder.ToTable("LibraryFolders", t => t.HasCheckConstraint("CK_LibraryFolders_Library", "\"Library\" IN ('parts', 'assemblies')"));
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Library).HasMaxLength(16).IsRequired();
        builder.Property(f => f.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(f => new { f.Library, f.ParentId });
        builder.HasOne<LibraryFolder>().WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}
