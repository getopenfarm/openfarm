using System.Globalization;
using FluentAssertions;
using OpenFarm.Application.FilamentTracking;

namespace OpenFarm.Application.Tests.FilamentTracking;

public class DailyUsageTests
{
    private static readonly TimeZoneInfo KualaLumpur = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");
    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly Guid Black = Guid.NewGuid();

    private static UsageEntry Draw(string utc, decimal grams, bool waste = false, Guid? variant = null) =>
        new(variant ?? Black, DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture), grams, waste);

    [Fact]
    public void Bucket_CountsEachDrawOnItsLocalDay_OldestFirst_TodayLast()
    {
        var usage = DailyUsage.Bucket(
            [Draw("2026-10-22T20:00:00Z", 100), Draw("2026-10-23T01:00:00Z", 50), Draw("2026-10-22T15:00:00Z", 10)], KualaLumpur, Today);

        usage.Days.Should().HaveCount(30);
        usage.Days[0].Should().Be(new DateOnly(2026, 9, 24));
        usage.Days[^1].Should().Be(Today);
        var black = usage.Colors.Should().ContainSingle().Subject;
        black.DrawnGrams[^1].Should().Be(150); // 04:00 and 09:00 local on the 23rd
        black.DrawnGrams[^2].Should().Be(10); // 23:00 local on the 22nd
    }

    [Fact]
    public void Bucket_DropsDrawsBeforeTheFirstDayAndAfterToday()
    {
        // 18:00 local on 23 Sep, the day before the window; 01:00 local on 24 Oct, tomorrow.
        var usage = DailyUsage.Bucket([Draw("2026-09-23T10:00:00Z", 100), Draw("2026-10-23T17:00:00Z", 100)], KualaLumpur, Today);

        usage.Colors.Should().BeEmpty();
    }

    [Fact]
    public void Bucket_KeepsWasteApart_PerColor()
    {
        var white = Guid.NewGuid();

        var usage = DailyUsage.Bucket(
            [Draw("2026-10-23T02:00:00Z", 100), Draw("2026-10-23T03:00:00Z", 40, waste: true), Draw("2026-10-23T04:00:00Z", 25, variant: white)],
            KualaLumpur, Today);

        var black = usage.Colors.Single(c => c.FilamentVariantId == Black);
        black.DrawnGrams[^1].Should().Be(140);
        black.WasteGrams[^1].Should().Be(40);
        usage.Colors.Single(c => c.FilamentVariantId == white).DrawnGrams[^1].Should().Be(25);
    }

    [Fact]
    public void Bucket_HandlesAZoneWhoseMidnightDoesNotExist()
    {
        // Chile springs forward at midnight: 00:00 on 6 Sep 2026 never happens there.
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        var usage = DailyUsage.Bucket([Draw("2026-09-06T04:30:00Z", 80)], santiago, new DateOnly(2026, 9, 6));

        usage.Colors.Should().ContainSingle().Which.DrawnGrams[^1].Should().Be(80);
    }
}
