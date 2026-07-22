using FluentAssertions;
using OpenFarm.Application.Profiles;

namespace OpenFarm.Application.Tests.Profiles;

// TDD (write-first) per plan.md's Constitution Check — the upsert-by-idempotency-key matcher is
// deterministic, well-specified logic (research.md §1).
public class SystemProfileIdentityKeyTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void Create_LeafPresetWithExternalId_IsLeafKeyedByExternalId()
    {
        var key = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", "GM001", "Bambu Lab X1 Carbon 0.4 nozzle");

        key.IsLeaf.Should().BeTrue();
        key.ExternalId.Should().Be("GM001");
        key.Name.Should().BeNull();
    }

    [Fact]
    public void Create_AbstractBasePresetWithNoExternalId_IsAbstractBaseKeyedByName()
    {
        var key = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", externalId: null, name: "fdm_bbl_3dp_001_common");

        key.IsLeaf.Should().BeFalse();
        key.Name.Should().Be("fdm_bbl_3dp_001_common");
        key.ExternalId.Should().BeNull();
    }

    [Fact]
    public void Create_BlankExternalId_TreatedTheSameAsNull()
    {
        var key = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Filament, "Bambulab", externalId: "   ", name: "fdm_filament_common");

        key.IsLeaf.Should().BeFalse();
        key.Name.Should().Be("fdm_filament_common");
    }

    [Fact]
    public void Create_SameLeafInputsTwice_ProducesEqualKeys()
    {
        var first = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Process, "Bambulab", "GP004", "0.20mm Standard @BBL X1C");
        var second = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Process, "Bambulab", "GP004", "0.20mm Bambu Support W @BBL X1C");

        // A rename between ingestion runs (research.md §1's renamed_from case) changes Name but not
        // ExternalId — the leaf key must still match, because Name isn't part of a leaf key at all.
        first.Should().Be(second);
    }

    [Fact]
    public void Create_DifferentKind_NeverEqualEvenWithIdenticalStrings()
    {
        var machineKey = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", "SHARED", "Shared Name");
        var processKey = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Process, "Bambulab", "SHARED", "Shared Name");

        machineKey.Should().NotBe(processKey);
    }

    [Fact]
    public void Create_DifferentOrganization_NeverEqualEvenWithIdenticalVendorAndExternalId()
    {
        var key1 = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", "GM001", "X1C");
        var key2 = SystemProfileIdentityKey.Create(Guid.NewGuid(), ProfileKind.Machine, "Bambulab", "GM001", "X1C");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Create_LeafKeyNeverEqualsAbstractBaseKeyEvenIfExternalIdEqualsSomeOtherRowsName()
    {
        // A leaf's ExternalId ("fdm_bbl_3dp_001_common") coincidentally matching another row's Name
        // must never collide — exactly one of ExternalId/Name is populated per key, so structural
        // equality alone keeps the two variants apart.
        var leafKey = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", "fdm_bbl_3dp_001_common", "Some Leaf");
        var abstractKey = SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "Bambulab", externalId: null, name: "fdm_bbl_3dp_001_common");

        leafKey.Should().NotBe(abstractKey);
    }

    [Fact]
    public void Create_BlankVendorName_Throws()
    {
        var act = () => SystemProfileIdentityKey.Create(OrgId, ProfileKind.Machine, "  ", "GM001", "Some Preset");

        act.Should().Throw<ArgumentException>();
    }
}
