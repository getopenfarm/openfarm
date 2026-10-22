using FluentAssertions;
using OpenFarm.Domain.FilamentTracking;

namespace OpenFarm.Domain.Tests.FilamentTracking;

public class SpoolRfidTests
{
    private const string Tag = "5F3D8925633343C29FB9A97FA9125971";

    private static Spool NewSpool() => Spool.Create(Guid.NewGuid(), null, 1000, 1000);

    [Fact]
    public void AttachRfid_UntaggedSpool_KeepsTheTrimmedId()
    {
        var spool = NewSpool();

        spool.AttachRfid($"  {Tag} ");

        spool.RfidId.Should().Be(Tag);
    }

    [Fact]
    public void AttachRfid_TheSameIdAgain_IsANoOp()
    {
        var spool = NewSpool();
        spool.AttachRfid(Tag);

        var again = () => spool.AttachRfid(Tag);

        again.Should().NotThrow();
        spool.RfidId.Should().Be(Tag);
    }

    [Fact]
    public void AttachRfid_WhenAnotherTagIsAttached_Throws()
    {
        var spool = NewSpool();
        spool.AttachRfid(Tag);

        var other = () => spool.AttachRfid("A77739E606CA41FAA0693A6A3BA63726");

        other.Should().Throw<SpoolRfidConflictException>();
        spool.RfidId.Should().Be(Tag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123X")]
    public void AttachRfid_BlankOrTooLong_Throws(string rfidId)
    {
        var attach = () => NewSpool().AttachRfid(rfidId);

        attach.Should().Throw<ArgumentException>();
    }
}
