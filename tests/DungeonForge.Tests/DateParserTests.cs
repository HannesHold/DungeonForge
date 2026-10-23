using DungeonForge.Services;

namespace DungeonForge.Tests;

public class DateParserTests
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    [Fact]
    public void ParsesExampleFromSpec_InServerTimeZone()
    {
        Assert.True(EventDateParser.TryParse("24.10.2026", "18:00", Vienna, TestSupport.Now, out var start, out var error), error);
        Assert.Equal(TimeSpan.FromHours(2), start.Offset);               // CEST on 24 Oct 2026
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 16, 0, 0, TimeSpan.Zero), start);
    }

    [Fact]
    public void UsesWinterOffsetAfterDstEnds()
    {
        Assert.True(EventDateParser.TryParse("26.10.2026", "18:00", Vienna, TestSupport.Now, out var start, out _));
        Assert.Equal(TimeSpan.FromHours(1), start.Offset);               // CET from 25 Oct 2026
    }

    [Theory]
    [InlineData("2026-10-24")]
    [InlineData("24/10/2026")]
    [InlineData("31.02.2027")]
    [InlineData("")]
    [InlineData("tomorrow")]
    public void RejectsBadDates(string date)
    {
        Assert.False(EventDateParser.TryParse(date, "18:00", Vienna, TestSupport.Now, out _, out var error));
        Assert.Contains("date", error);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("18.00")]
    [InlineData("6pm")]
    [InlineData("")]
    public void RejectsBadTimes(string time)
    {
        Assert.False(EventDateParser.TryParse("24.10.2026", time, Vienna, TestSupport.Now, out _, out var error));
        Assert.Contains("time", error);
    }

    [Fact]
    public void AcceptsSingleDigitVariants()
    {
        Assert.True(EventDateParser.TryParse("4.11.2026", "9:30", Vienna, TestSupport.Now, out var start, out _));
        Assert.Equal(new DateTime(2026, 11, 4, 9, 30, 0), start.DateTime);
    }

    [Fact]
    public void RejectsPast_ButAcceptsLaterToday()
    {
        Assert.False(EventDateParser.TryParse("23.10.2026", "08:00", Vienna, TestSupport.Now, out _, out var error));
        Assert.Contains("past", error);
        Assert.True(EventDateParser.TryParse("23.10.2026", "20:00", Vienna, TestSupport.Now, out _, out _));
    }

    [Fact]
    public void RejectsNonExistentTimeInDstGap()
    {
        // 28 Mar 2027: 02:00 -> 03:00 in Europe/Vienna
        Assert.False(EventDateParser.TryParse("28.03.2027", "02:30", Vienna, TestSupport.Now, out _, out var error));
        Assert.Contains("does not exist", error);
    }
}
