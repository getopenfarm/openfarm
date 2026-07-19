using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.ToTable("Parts");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired();

        builder.Property(p => p.FileFormat).IsRequired().HasConversion<string>();
        builder.Property(p => p.RequiresSlicing).IsRequired();
        builder.Property(p => p.OriginalFileName).IsRequired();
        builder.Property(p => p.StoragePath).IsRequired();
        builder.Property(p => p.FileSizeBytes).IsRequired();
        builder.Property(p => p.FileUploadedAtUtc).IsRequired();

        builder.Property(p => p.NozzleDiameterMm).IsRequired();

        builder.Property(p => p.EstimateStatus).IsRequired().HasConversion<string>();
        builder.Property(p => p.EstimatedPrintTimeMinutes);
        builder.Property(p => p.EstimatedFilamentWeightGrams);

        builder.Property(p => p.SlicerEngineOverride).HasConversion<string>();

        builder.Property(p => p.PlateCount).IsRequired();

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc).IsRequired();

        builder.HasMany(p => p.FilamentRequirements)
            .WithOne()
            .HasForeignKey(r => r.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.FilamentRequirements).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(p => p.Thumbnails)
            .WithOne()
            .HasForeignKey(t => t.PartId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Thumbnails).UsePropertyAccessMode(PropertyAccessMode.Field);

        // PartTags is a plain FK-derived join between two already tenant-filtered aggregates — no
        // independent OrganizationId shadow column (EF's skip-navigation insert path has no natural
        // source to populate one from on Part.AddTag, unlike every real child/root entity in this
        // feature, which does carry its own OrganizationId + IsMultiTenant()).
        builder.HasMany(p => p.Tags)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "PartTags",
                j => j.HasOne<Tag>().WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<Part>().WithMany().HasForeignKey("PartId").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("PartId", "TagId");
                    j.ToTable("PartTags");
                });
        builder.Navigation(p => p.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Postgres system column; the Npgsql provider auto-maps a "xmin"-named uint row-version
        // property to it (data-model.md's optimistic-concurrency requirement, same technique Spool uses).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.IsMultiTenant();
    }
}
