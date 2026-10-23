using DungeonForge.Services;

namespace DungeonForge.Tests;

public class DeletionAuditTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 17, 40, 0, TimeSpan.Zero);

    [Fact]
    public void FindActor_MatchesTheNewestThreadDeleteInsideTheWindow()
    {
        var entries = new[]
        {
            Row(actor: 1, age: TimeSpan.FromSeconds(10), threadId: 50),
            Row(actor: 2, age: TimeSpan.FromSeconds(2), threadId: 50),
            Row(actor: 3, age: TimeSpan.FromSeconds(1), threadId: 99),
            Row(actor: 4, age: DeletionAudit.Window + TimeSpan.FromTicks(1), threadId: 50),
        };

        Assert.Equal(2UL, DeletionAudit.FindActor(entries, threadId: 50, channelId: null, messageAuthorId: null, Now));
    }

    [Fact]
    public void FindActor_AcceptsAThreadDeleteAFewSecondsAheadOfTheBotClock()
    {
        var entries = new[] { Row(actor: 7, age: TimeSpan.FromSeconds(-4), threadId: 50) };
        Assert.Equal(7UL, DeletionAudit.FindActor(entries, threadId: 50, channelId: null, messageAuthorId: null, Now));

        var tooFar = new[] { Row(actor: 7, age: -(DeletionAudit.FutureSkew + TimeSpan.FromTicks(1)), threadId: 50) };
        Assert.Null(DeletionAudit.FindActor(tooFar, threadId: 50, channelId: null, messageAuthorId: null, Now));
    }

    [Fact]
    public void FindActor_MatchesAMessageDeleteByChannelAndAuthor()
    {
        var entries = new[]
        {
            Row(actor: 5, age: TimeSpan.FromSeconds(1), channelId: 10, authorId: 100),
            Row(actor: 6, age: TimeSpan.FromSeconds(1), channelId: 10, authorId: 200),
            Row(actor: 8, age: TimeSpan.FromSeconds(3), channelId: 10, authorId: 100),
            Row(actor: 9, age: TimeSpan.FromSeconds(0), channelId: 11, authorId: 100),
        };

        Assert.Equal(5UL, DeletionAudit.FindActor(entries, threadId: null, channelId: 10, messageAuthorId: 100, Now));
    }

    [Fact]
    public void FindActor_AcceptsAMessageDeleteWhoseAuthorDiscordLeftOut()
    {
        var entries = new[] { Row(actor: 5, age: TimeSpan.FromSeconds(1), channelId: 10, authorId: null) };
        Assert.Equal(5UL, DeletionAudit.FindActor(entries, threadId: null, channelId: 10, messageAuthorId: 100, Now));
    }

    [Fact]
    public void FindActor_PrefersTheThreadDeleteWhenTheForumPostMatchesBoth()
    {
        var entries = new[]
        {
            Row(actor: 5, age: TimeSpan.FromSeconds(1), channelId: 50, authorId: 100),
            Row(actor: 4, age: TimeSpan.FromSeconds(4), threadId: 50),
        };

        Assert.Equal(4UL, DeletionAudit.FindActor(entries, threadId: 50, channelId: 50, messageAuthorId: 100, Now));
    }

    [Fact]
    public void FindActor_IgnoresARowWithNoActor()
    {
        var entries = new[] { Row(actor: 0, age: TimeSpan.Zero, threadId: 50) };
        Assert.Null(DeletionAudit.FindActor(entries, threadId: 50, channelId: null, messageAuthorId: null, Now));
    }

    [Theory]
    [InlineData(42UL, " by <@42>")]
    [InlineData(0UL, " (deleter unknown)")]
    public void WithActor_AppendsTheActorToTheExistingSentence(ulong actorId, string suffix)
    {
        var line = DeletionAudit.RemovalLine("thread", "Molten Core", "24.10.2026 18:00");
        var expected = "The thread of event **Molten Core** (24.10.2026 18:00) was deleted, so the event was removed" + suffix + ".";
        Assert.Equal(expected, DeletionAudit.WithActor(line, actorId == 0 ? null : actorId));
    }

    [Fact]
    public void WithActor_UsesDeleterUnknownWhenTheActorIdIsZero()
    {
        var line = DeletionAudit.RemovalLine("message", "Molten Core", "24.10.2026 18:00");
        Assert.EndsWith("was removed (deleter unknown).", DeletionAudit.WithActor(line, 0));
    }

    private static DeletionAudit.Entry Row(
        ulong actor,
        TimeSpan age,
        ulong? threadId = null,
        ulong? channelId = null,
        ulong? authorId = null) =>
        new(actor, Now - age, threadId, channelId, authorId);
}
