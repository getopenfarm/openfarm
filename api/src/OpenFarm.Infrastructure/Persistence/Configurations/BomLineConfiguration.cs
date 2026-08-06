using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenFarm.Domain.Assemblies;
using OpenFarm.Domain.Parts;

namespace OpenFarm.Infrastructure.Persistence.Configurations;

/// <summary>
/// data-model.md's full table shape — a discriminator (<see cref="BomLine.TargetKind"/>) plus three
/// nullable FK columns, CHECK-constrained to exactly one set and consistent with the kind (FR-003),
/// three partial unique indexes backstopping FR-013, and a second, independently-configured FK to
/// <c>Assemblies</c> for <see cref="BomLine.TargetAssemblyId"/> (Restrict) alongside the owning
/// <see cref="BomLine.AssemblyId"/> FK configured on the <c>Assembly</c> side (Cascade,
/// <see cref="AssemblyConfiguration"/>).
/// </summary>
public sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> builder)
    {
        builder.ToTable("BomLines", t =>
        {
            t.HasCheckConstraint(
                "CK_BomLines_ExactlyOneTargetMatchingKind",
                """
                ("TargetKind" = 'Part' AND "TargetPartId" IS NOT NULL AND "TargetAssemblyId" IS NULL AND "TargetNonPrintedComponentId" IS NULL) OR
                ("TargetKind" = 'Assembly' AND "TargetAssemblyId" IS NOT NULL AND "TargetPartId" IS NULL AND "TargetNonPrintedComponentId" IS NULL) OR
                ("TargetKind" = 'NonPrintedComponent' AND "TargetNonPrintedComponentId" IS NOT NULL AND "TargetPartId" IS NULL AND "TargetAssemblyId" IS NULL)
                """);

            t.HasCheckConstraint(
                "CK_BomLines_NoDirectSelfReference",
                """
                "TargetAssemblyId" IS NULL OR "TargetAssemblyId" <> "AssemblyId"
                """);
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.OrganizationId).IsRequired();
        builder.Property(l => l.AssemblyId).IsRequired();
        builder.Property(l => l.TargetKind).IsRequired().HasConversion<string>();
        builder.Property(l => l.TargetPartId);
        builder.Property(l => l.TargetAssemblyId);
        builder.Property(l => l.TargetNonPrintedComponentId);
        builder.Property(l => l.Quantity).IsRequired().HasColumnType("numeric");

        builder.HasOne<Part>().WithMany().HasForeignKey(l => l.TargetPartId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Assembly>().WithMany().HasForeignKey(l => l.TargetAssemblyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<NonPrintedComponent>().WithMany().HasForeignKey(l => l.TargetNonPrintedComponentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.AssemblyId, l.TargetPartId }).IsUnique().HasFilter("\"TargetPartId\" IS NOT NULL");
        builder.HasIndex(l => new { l.AssemblyId, l.TargetAssemblyId }).IsUnique().HasFilter("\"TargetAssemblyId\" IS NOT NULL");
        builder.HasIndex(l => new { l.AssemblyId, l.TargetNonPrintedComponentId }).IsUnique().HasFilter("\"TargetNonPrintedComponentId\" IS NOT NULL");

        builder.IsMultiTenant();
    }
}
