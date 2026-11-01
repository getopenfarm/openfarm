using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

public sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.ToTable("Parts");

        builder.HasOne<OpenFarm.Domain.Library.LibraryFolder>().WithMany().HasForeignKey(p => p.FolderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.FolderId);

        builder.HasKey(p => p.Id);


        builder.Property(p => p.Name).IsRequired();

        builder.Property(p => p.FileFormat).IsRequired().HasConversion<string>();
        builder.Property(p => p.RequiresSlicing).IsRequired();
        builder.Property(p => p.OriginalFileName).IsRequired();
        builder.Property(p => p.StoragePath).IsRequired();
        builder.Property(p => p.FileSizeBytes).IsRequired();
        builder.Property(p => p.FileUploadedAtUtc).IsRequired();

        builder.Property(p => p.NozzleDiameterMm);
        builder.Property(p => p.MaterialMetadataComplete).HasDefaultValue(true);
        builder.Property(p => p.ActiveProcessingJobId);
        builder.Property(p => p.EstimateUnavailableReason);
        builder.Property(p => p.EstimationDetailsJson).HasColumnType("jsonb");
        builder.Ignore(p => p.Estimation);
        builder.Property(p => p.ObjectManifestJson).HasColumnType("jsonb");
        builder.Property(p => p.MetadataWarningsJson).HasColumnType("jsonb");
        builder.Ignore(p => p.MetadataWarnings);
        builder.Ignore(p => p.PrintRequirementsStatus);
        builder.Ignore(p => p.ProcessingWarnings);

        builder.Property(p => p.EstimateStatus).IsRequired().HasConversion<string>();
        builder.Property(p => p.EstimatedPrintTimeMinutes);
        builder.Property(p => p.EstimatedFilamentWeightGrams);

        builder.Property(p => p.SlicerEngineOverride).HasConversion<string>();
        builder.Property(p => p.SlicerVersionUsed);

        builder.Property(p => p.PlateNumber);
        builder.Property(p => p.PlateName);

        builder.Property(p => p.UnitsPerPrint).IsRequired();
        builder.Property(p => p.RevisionHash).IsRequired();
        builder.Property(p => p.RevisionNumber).IsRequired();

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

        // PartTags is a plain FK-derived join, mapped as a real CLR type (PartTagJoinRow) rather than
        // Dictionary<string,object> — the more conventional EF Core pattern.
        builder.HasMany(p => p.Tags)
            .WithMany()
            .UsingEntity<PartTagJoinRow>(
                j => j.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade),
                j => j.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey(x => new { x.PartId, x.TagId });
                    j.ToTable("PartTags");
                });
        builder.Navigation(p => p.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Postgres system column; the Npgsql provider auto-maps a "xmin"-named uint row-version
        // property to it (data-model.md's optimistic-concurrency requirement, same technique Spool uses).
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
