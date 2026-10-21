namespace OpenFarm.Domain.Profiles;

/// <summary>
/// What the machine, process and filament preset rows have in common: where they came from and how a System row
/// is identified inside one slicer-version catalog. Lets version mapping and identity comparison treat the three
/// kinds alike.
/// </summary>
public interface ICatalogProfile
{
    Guid Id { get; }

    string Name { get; }

    ProfileOrigin Origin { get; }

    ProfileSourceFormat SourceFormat { get; }

    /// <summary>The slicer version a System row was ingested from; always set for System rows, <c>null</c> for User rows.</summary>
    string? SourceVersion { get; }

    string? VendorName { get; }

    /// <summary>The vendor's stable <c>setting_id</c>; <c>null</c> for abstract bases and name-keyed presets.</summary>
    string? ExternalId { get; }

    /// <summary><c>false</c> once a re-ingestion of the row's catalog no longer contains it; always <c>true</c> for User rows.</summary>
    bool IsCurrentlyOffered { get; }

    /// <summary>Whether the preset may be picked (<c>false</c> for abstract bases and template fragments).</summary>
    bool IsSelectable { get; }
}
