using FluentAssertions;
using OpenFarm.Application.Parts;
using OpenFarm.Application.Profiles;
using OpenFarm.Application.Tests.Profiles.Fakes;
using OpenFarm.Domain.FilamentTracking;
using OpenFarm.Domain.Parts;
using OpenFarm.Domain.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// TDD (write-first) — research.md §2/FR-018/FR-019 (System-catalog tier) plus the tiered rewrite from
// 010-filament-matching (research.md R2/R3/R4): the deterministic tie-break, at every tier, must be
// stable across repeated calls and across ingestion re-runs (keyed on Id/VendorName+ExternalId, never
// the human-readable Name).
public class RepresentativeFilamentResolverTests
{
    private static FilamentProfile CreateSystemFilament(
        string vendorName, string externalId, string materialType, bool isSelectable = true, bool isCurrentlyOffered = true)
    {
        var profile = FilamentProfile.CreateSystemProfile(
            Guid.NewGuid(), $"{vendorName} {materialType} {externalId}", ProfileSourceFormat.OrcaSlicer, null,
            $$"""{"type":"filament","filament_type":["{{materialType}}"]}""",
            vendorName, externalId, isSelectable, compatibleMachineNames: null, materialType: materialType, includedTemplateNames: null);

        if (!isCurrentlyOffered)
        {
            profile.MarkStale();
        }

        return profile;
    }

    private static RepresentativeFilamentResolver CreateResolver(
        FakeFilamentProfileRepository filamentProfileRepo, FakeSpoolRepository spoolRepo, FakeFilamentRepository filamentRepo, FakeFeedUnitRepository feedUnitRepo) =>
        new(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

    /// <summary>
    /// Registers a Filament (linked to <paramref name="filamentProfile"/> via
    /// <c>FilamentProfileId</c>) with one Variant, a Spool of that variant, and — when
    /// <paramref name="printerId"/> is given — a FeedUnit/FeedSlot on that printer with the spool
    /// loaded onto it (<c>SpoolState.Using</c>); otherwise the spool is left <c>InStorage</c>
    /// (the filament-library tier).
    /// </summary>
    private static async Task<Spool> SeedSpoolAsync(
        FakeFilamentRepository filamentRepo, FakeSpoolRepository spoolRepo, FakeFeedUnitRepository feedUnitRepo,
        string materialType, FilamentProfile filamentProfile, Guid? printerId)
    {
        var filament = Filament.Create(Guid.NewGuid(), Guid.NewGuid(), materialType, materialType, filamentProfile.Id);
        var variant = filament.AddVariant("Black", "#000000");
        await filamentRepo.AddAsync(filament, CancellationToken.None);

        var spool = Spool.Create(Guid.NewGuid(), variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(spool, CancellationToken.None);

        if (printerId is { } targetPrinterId)
        {
            var feedUnit = FeedUnit.CreateConnectorDerived(Guid.NewGuid(), targetPrinterId, FeedUnitKindCatalog.ExternalSpool, unitIndex: 0);
            var feedSlot = feedUnit.AddFeedSlot(slotPosition: 0);
            await feedUnitRepo.AddAsync(feedUnit, CancellationToken.None);
            spool.LoadOntoFeedSlot(feedSlot.Id);
        }

        return spool;
    }

    [Fact]
    public async Task ResolveAsync_MultipleCandidatesForSameMaterial_PicksSmallestVendorNameThenExternalIdOrdinal()
    {
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Zzz Vendor", "Z001", "PLA"), CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(expected, CancellationToken.None);
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "B001", "PLA"), CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(expected.Id);
        result.Source.Should().Be(FilamentMatchSource.SystemCatalog);
    }

    [Fact]
    public async Task ResolveAsync_RepeatedCalls_ReturnIdenticalResult()
    {
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA"), CancellationToken.None);
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "B001", "PLA"), CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var first = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);
        var second = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        first.Profile.Id.Should().Be(second.Profile.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoCandidateForMaterial_ThrowsSlicingProfileUnconfigured()
    {
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PETG"), CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var act = async () => await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        await act.Should().ThrowAsync<SlicingProfileUnconfiguredException>();
    }

    [Fact]
    public async Task ResolveAsync_MatchingCandidateNotCurrentlyOffered_IsExcluded()
    {
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA", isCurrentlyOffered: false), CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "B001", "PLA");
        await filamentProfileRepo.AddAsync(expected, CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task ResolveAsync_UserOriginCandidateForSameMaterial_IsExcluded()
    {
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var userProfile = FilamentProfile.Create(Guid.NewGuid(), "My Tuned PLA", ProfileSourceFormat.OrcaSlicer, null, """{"filament_type":["PLA"]}""");
        await filamentProfileRepo.AddAsync(userProfile, CancellationToken.None);
        var expected = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(expected, CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task ResolveAsync_NoPrinterTargeted_FleetLoadedSpoolWinsOverSystemCatalog()
    {
        // Scenario 1 — a fleet-wide loaded spool wins over unrelated System profiles when no printer is targeted.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA"), CancellationToken.None);

        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();
        var loadedProfile = CreateSystemFilament("Polymaker", "P001", "PLA");
        await filamentProfileRepo.AddAsync(loadedProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", loadedProfile, printerId: Guid.NewGuid());

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(loadedProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.FleetLoaded);
    }

    [Fact]
    public async Task ResolveAsync_TargetPrinterSpecified_OwnLoadedSpoolWinsOverFleetWideLoadedSpool()
    {
        // Scenario 3 — the target printer's own loaded spool wins over another fleet printer's.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var targetPrinterId = Guid.NewGuid();
        var otherPrinterId = Guid.NewGuid();

        var otherPrinterProfile = CreateSystemFilament("Polymaker", "P001", "PLA");
        await filamentProfileRepo.AddAsync(otherPrinterProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", otherPrinterProfile, otherPrinterId);

        var targetPrinterProfile = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(targetPrinterProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", targetPrinterProfile, targetPrinterId);

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(targetPrinterId, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(targetPrinterProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.FleetLoaded);
    }

    [Fact]
    public async Task ResolveAsync_NoPrinterTargeted_TwoPrintersLoaded_StableAcrossRepeatedCalls()
    {
        // Scenario 2 — the fleet-wide pick is stable across repeated calls with two printers loaded.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var profileA = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(profileA, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", profileA, Guid.NewGuid());

        var profileB = CreateSystemFilament("Polymaker", "P001", "PLA");
        await filamentProfileRepo.AddAsync(profileB, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", profileB, Guid.NewGuid());

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var first = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);
        var second = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        first.Profile.Id.Should().Be(second.Profile.Id);
        first.Source.Should().Be(FilamentMatchSource.FleetLoaded);
    }

    [Fact]
    public async Task ResolveAsync_LoadedSpoolWrongMaterial_IsNeverChosen()
    {
        // Scenario 4 — a wrong-material loaded spool is never chosen, even though it's the only fleet-loaded spool.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var petgProfile = CreateSystemFilament("Bambulab", "A001", "PETG");
        await filamentProfileRepo.AddAsync(petgProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PETG", petgProfile, Guid.NewGuid());

        var plaCatalogProfile = CreateSystemFilament("Bambulab", "A002", "PLA");
        await filamentProfileRepo.AddAsync(plaCatalogProfile, CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(plaCatalogProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.SystemCatalog);
    }

    [Fact]
    public async Task ResolveAsync_FleetLoadedSpool_NoPrinterReachabilitySignalNeeded_StillWins()
    {
        // research.md R4 — the resolver has no IPrinterRepository dependency at all: a loaded spool's
        // printer being "unreachable" isn't even representable here, by construction. This documents
        // that guarantee: resolution succeeds purely off FeedUnit/Spool state, no Printer row required.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var loadedProfile = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(loadedProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", loadedProfile, Guid.NewGuid());

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(loadedProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.FleetLoaded);
    }

    [Fact]
    public async Task ResolveAsync_NoFleetLoadedSpool_StoredLibrarySpoolWinsOverSystemCatalog()
    {
        // US3 Scenario 1 — a stored (InStorage) matching spool wins over an unrelated System profile
        // when nothing is fleet-loaded.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        await filamentProfileRepo.AddAsync(CreateSystemFilament("Bambulab", "A001", "PLA"), CancellationToken.None);

        var storedProfile = CreateSystemFilament("Polymaker", "P001", "PLA");
        await filamentProfileRepo.AddAsync(storedProfile, CancellationToken.None);
        await SeedSpoolAsync(filamentRepo, spoolRepo, feedUnitRepo, "PLA", storedProfile, printerId: null);

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(storedProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.FilamentLibrary);
    }

    [Fact]
    public async Task ResolveAsync_NeitherLoadedNorStoredSpool_FallsBackToSystemCatalog()
    {
        // US3 Scenario 2 — falls back to the System catalog when neither a loaded nor a stored spool exists.
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var catalogProfile = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(catalogProfile, CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, new FakeSpoolRepository(), new FakeFilamentRepository(), new FakeFeedUnitRepository());

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(catalogProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.SystemCatalog);
    }

    [Fact]
    public async Task ResolveAsync_StoredSpoolWithDanglingFilamentProfileLink_IsSkippedNotAnError()
    {
        // US3 Scenario 3 — a stored spool whose Filament.FilamentProfileId doesn't resolve to any real
        // row is skipped, proceeding to the System catalog fallback rather than throwing (FR-010).
        var filamentProfileRepo = new FakeFilamentProfileRepository();
        var spoolRepo = new FakeSpoolRepository();
        var filamentRepo = new FakeFilamentRepository();
        var feedUnitRepo = new FakeFeedUnitRepository();

        var catalogProfile = CreateSystemFilament("Bambulab", "A001", "PLA");
        await filamentProfileRepo.AddAsync(catalogProfile, CancellationToken.None);

        // Dangling: FilamentProfileId points at a Guid with no matching row in filamentProfileRepo.
        var danglingFilament = Filament.Create(Guid.NewGuid(), Guid.NewGuid(), "PLA", "PLA", Guid.NewGuid());
        var variant = danglingFilament.AddVariant("Black", "#000000");
        await filamentRepo.AddAsync(danglingFilament, CancellationToken.None);

        var danglingSpool = Spool.Create(Guid.NewGuid(), variant.Id, label: null, spoolWeightGrams: 1000, remainingWeightGrams: 800);
        await spoolRepo.AddAsync(danglingSpool, CancellationToken.None);

        var resolver = CreateResolver(filamentProfileRepo, spoolRepo, filamentRepo, feedUnitRepo);

        var result = await resolver.ResolveAsync(printerId: null, "PLA", CancellationToken.None);

        result.Profile.Id.Should().Be(catalogProfile.Id);
        result.Source.Should().Be(FilamentMatchSource.SystemCatalog);
    }
}
