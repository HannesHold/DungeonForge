using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class PointsSystemTests
{
    private static readonly DateTimeOffset T0 = TestSupport.Now;

    [Fact]
    public void DungeonRejectsPoints_RaidDefaultsOn()
    {
        Assert.NotNull(PointsLogic.DungeonPointsError(EventType.Dungeon, true));
        Assert.Null(PointsLogic.DungeonPointsError(EventType.Dungeon, false));
        Assert.Null(PointsLogic.DungeonPointsError(EventType.Dungeon, null));
        Assert.Null(PointsLogic.DungeonPointsError(EventType.Raid, true));

        Assert.False(PointsLogic.AwardsPointsForNewEvent(EventType.Dungeon, null));
        Assert.False(PointsLogic.AwardsPointsForNewEvent(EventType.Dungeon, false));
        Assert.True(PointsLogic.AwardsPointsForNewEvent(EventType.Raid, null));
        Assert.True(PointsLogic.AwardsPointsForNewEvent(EventType.Raid, true));
        Assert.False(PointsLogic.AwardsPointsForNewEvent(EventType.Raid, false));
    }

    [Theory]
    [InlineData(10, 25, 3)]
    [InlineData(10, 50, 5)]
    [InlineData(1, 25, 0)]
    [InlineData(2, 25, 1)]
    [InlineData(1, 50, 1)]
    [InlineData(10, 0, 0)]
    [InlineData(10, 100, 10)]
    [InlineData(7, 25, 2)]
    public void BenchShare_RoundsHalfAwayFromZero_AndNeverBelowZero(int bossPoints, int percent, int expected) =>
        Assert.Equal(expected, PointsLogic.BenchShare(bossPoints, percent));

    [Fact]
    public void ResolvePoints_UsesOverrideOrGlobalDefault()
    {
        Assert.Equal(10, PointsLogic.ResolvePoints(new BossDefinition { Name = "A" }, 10));
        Assert.Equal(15, PointsLogic.ResolvePoints(new BossDefinition { Name = "A", Points = 15 }, 10));
        Assert.Equal(10, PointsLogic.ResolvePoints(null, 10));
    }

    [Fact]
    public void FullRaid_PaysAttendeesAndBench_CantAttendGetsNothing()
    {
        var e = RaidWithRoster(full: true, bench: 2, cantAttend: 1);
        var bosses = Bosses("Lucifron", "Magmadar");
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", bosses, T0).Ok);
        Assert.True(PointsLogic.RecordKill(e, "magmadar", bosses, T0.AddMinutes(1)).Ok);

        var kill = e.Kills[0];
        Assert.True(kill.RaidWasFull);
        Assert.Equal(40, kill.Attendees.Count);
        Assert.Equal(2, kill.Bench.Count);
        Assert.DoesNotContain(kill.Attendees, a => a.UserId == 900);
        Assert.DoesNotContain(kill.Bench, a => a.UserId == 900);

        var entries = Pay(e);
        Assert.Equal(20, PointsLogic.Total(entries, 1));
        Assert.Equal(6, PointsLogic.Total(entries, 500));
        Assert.Equal(6, PointsLogic.Total(entries, 501));
        Assert.Equal(0, PointsLogic.Total(entries, 900));
        Assert.Equal(40 * 2 + 2 * 2, entries.Count);
        Assert.All(entries.Where(entry => entry.UserId == 500), entry => Assert.Equal(PointsLogic.BenchRole, entry.Role));
        Assert.Contains(entries, entry => entry.UserId == 1 && entry.Role == "Tank");
    }

    [Fact]
    public void NotFullRaid_BenchGetsNothing()
    {
        var e = RaidWithRoster(full: false, bench: 3, cantAttend: 1);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron"), T0).Ok);
        Assert.False(e.Kills[0].RaidWasFull);
        Assert.Equal(3, e.Kills[0].Bench.Count);

        var entries = Pay(e);
        Assert.Equal(10, PointsLogic.Total(entries, 1));
        Assert.DoesNotContain(entries, entry => entry.Role == PointsLogic.BenchRole);
        Assert.Equal(0, PointsLogic.Total(entries, 500));
    }

    [Fact]
    public void BenchRule_IsPerKill()
    {
        var e = RaidWithRoster(full: true, bench: 1, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron", "Magmadar"), T0).Ok);

        e.Signups.RemoveAll(s => s.UserId == 40);
        Assert.True(PointsLogic.RecordKill(e, "Magmadar", Bosses("Lucifron", "Magmadar"), T0.AddMinutes(30)).Ok);
        Assert.False(e.Kills[1].RaidWasFull);
        Assert.Contains(e.Kills[1].Bench, b => b.UserId == 500);

        var entries = Pay(e);
        Assert.Equal(20, PointsLogic.Total(entries, 1));
        Assert.Equal(3, PointsLogic.Total(entries, 500));
        Assert.Equal(2, PointsLogic.PlayerTotals(entries).Single(p => p.UserId == 1).Kills);
        Assert.Equal(1, PointsLogic.PlayerTotals(entries).Single(p => p.UserId == 500).Kills);
    }

    [Fact]
    public void Leaver_GetsOnlyEarlierKills_EvenIfStillSignedUp()
    {
        var e = RaidWithRoster(full: true, bench: 0, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron", "Magmadar"), T0).Ok);
        Assert.True(PointsLogic.Exclude(e, 7).Ok);
        Assert.Contains(e.Signups, s => s.UserId == 7);
        Assert.True(PointsLogic.RecordKill(e, "Magmadar", Bosses("Lucifron", "Magmadar"), T0.AddHours(1)).Ok);

        Assert.Contains(e.Kills[0].Attendees, a => a.UserId == 7);
        Assert.DoesNotContain(e.Kills[1].Attendees, a => a.UserId == 7);
        Assert.True(e.Kills[1].RaidWasFull);

        var entries = Pay(e);
        Assert.Equal(10, PointsLogic.Total(entries, 7));
        Assert.Equal(1, PointsLogic.PlayerTotals(entries).Single(p => p.UserId == 7).Kills);
        Assert.Equal(20, PointsLogic.Total(entries, 1));
    }

    [Fact]
    public void LateJoiner_IsIncludedOnlyAfterTheExclusionIsRemoved()
    {
        var e = RaidWithRoster(full: false, bench: 1, cantAttend: 0);
        SignupLogic.Apply(e, 77, "Heal", T0);
        Assert.True(PointsLogic.Exclude(e, 77).Ok);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron", "Magmadar"), T0).Ok);
        Assert.DoesNotContain(e.Kills[0].Attendees, a => a.UserId == 77);

        var notSigned = PointsLogic.Include(e, 12345);
        Assert.False(notSigned.Ok);
        Assert.Contains("Bench", notSigned.Message);

        Assert.True(PointsLogic.Include(e, 77).Ok);
        Assert.True(PointsLogic.RecordKill(e, "Magmadar", Bosses("Lucifron", "Magmadar"), T0.AddHours(1)).Ok);
        Assert.Contains(e.Kills[1].Attendees, a => a.UserId == 77);

        var entries = Pay(e);
        Assert.Equal(10, PointsLogic.Total(entries, 77));
        Assert.Equal(1, PointsLogic.PlayerTotals(entries).Single(p => p.UserId == 77).Kills);
    }

    [Fact]
    public void EditKill_AddsAndRemovesOneKillAttendee()
    {
        var e = RaidWithRoster(full: false, bench: 1, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron"), T0).Ok);
        Assert.True(PointsLogic.EditAttendee(e, "Lucifron", 500, add: true).Ok);
        Assert.DoesNotContain(e.Kills[0].Bench, b => b.UserId == 500);
        Assert.Contains(e.Kills[0].Attendees, a => a.UserId == 500);

        var added = Pay(e);
        Assert.Equal(10, PointsLogic.Total(added, 500));
        Assert.Contains(added, entry => entry.UserId == 500 && entry.Role == PointsLogic.ManualAttendeeRole);
        Assert.DoesNotContain(added, entry => entry.UserId == 500 && entry.Role == PointsLogic.BenchRole);

        Assert.True(PointsLogic.EditAttendee(e, "Lucifron", 500, add: false).Ok);
        Assert.False(PointsLogic.EditAttendee(e, "Lucifron", 500, add: false).Ok);
        var removed = Pay(e);
        Assert.Equal(0, PointsLogic.Total(removed, 500));
    }

    [Fact]
    public void Completion_IsIdempotent_AndZeroKillsPayNothing()
    {
        var e = RaidWithRoster(full: true, bench: 1, cantAttend: 0);
        e.AwardsPoints = true;
        var points = new Func<string, int>(_ => 10);

        var none = PointsLogic.Complete(e, points, 25, T0, ledgerAlreadyHasEvent: false);
        Assert.False(none.AlreadyCompleted);
        Assert.Empty(none.Entries);
        Assert.Equal(EventStatus.Completed, e.Status);
        Assert.Equal(T0, e.CompletedAt);

        var again = PointsLogic.Complete(e, points, 25, T0.AddMinutes(1), ledgerAlreadyHasEvent: false);
        Assert.True(again.AlreadyCompleted);
        Assert.Empty(again.Entries);
        Assert.Equal(T0, e.CompletedAt);

        var open = RaidWithRoster(full: true, bench: 0, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(open, "Lucifron", Bosses("Lucifron"), T0).Ok);
        var recovered = PointsLogic.Complete(open, points, 25, T0, ledgerAlreadyHasEvent: true);
        Assert.True(recovered.AlreadyCompleted);
        Assert.Empty(recovered.Entries);
        Assert.Equal(EventStatus.Completed, open.Status);

        var plain = TestSupport.Event40();
        plain.AwardsPoints = false;
        var marked = PointsLogic.Complete(plain, points, 25, T0, false);
        Assert.False(marked.AlreadyCompleted);
        Assert.Empty(marked.Entries);
        Assert.Equal(EventStatus.Completed, plain.Status);
    }

    [Fact]
    public void PerBossOverride_ReplacesTheGlobalDefault()
    {
        var e = RaidWithRoster(full: false, bench: 0, cantAttend: 0);
        var bosses = new List<BossDefinition>
        {
            new() { Name = "Lucifron" },
            new() { Name = "Magmadar", Points = 7 },
        };
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", bosses, T0).Ok);
        Assert.True(PointsLogic.RecordKill(e, "Magmadar", bosses, T0).Ok);
        var entries = PointsLogic.BuildEntries(e, boss => PointsLogic.ResolvePoints(bosses.First(b => b.Name == boss), 10), 25, T0);
        Assert.Equal(17, PointsLogic.Total(entries, 1));
    }

    [Fact]
    public void Leaderboard_OrdersByTotalThenUserId()
    {
        var entries = new List<PointsLedgerEntry>
        {
            Entry(5, 10, "a"),
            Entry(2, 10, "b"),
            Entry(9, 30, "c"),
            Entry(4, 10, "d"),
            Entry(4, 0, "e"),
        };
        var board = PointsLogic.Leaderboard(entries);
        Assert.Equal(new[] { 9UL, 2UL, 4UL, 5UL }, board.Select(row => row.UserId));
        Assert.Equal(new[] { 30, 10, 10, 10 }, board.Select(row => row.Total));
    }

    [Fact]
    public void Summary_ShowsKillsWorthOfPoints_AndChunks()
    {
        var e = RaidWithRoster(full: true, bench: 1, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron", "Magmadar"), T0).Ok);
        Assert.True(PointsLogic.RecordKill(e, "Magmadar", Bosses("Lucifron", "Magmadar"), T0).Ok);
        var entries = Pay(e);
        var messages = PointsSummaryFormatter.FormatCompletion(e, false, entries, _ => 10, maxLength: 180);
        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 180));
        var text = string.Join('\n', messages);
        Assert.Contains("Lucifron (10)", text);
        Assert.Contains("2 kills", text);
        Assert.Contains("<@500>", text);
    }

    [Fact]
    public void LeaderboardFormatter_ListsEveryone_WithContinuousRanks_AndBossTableChunks()
    {
        var ranked = Enumerable.Range(1, 30).Select(i => ((ulong)i, 100 - i)).ToList();
        var pages = PointsBoardFormatter.FormatLeaderboard(ranked, maxLength: 80);
        Assert.True(pages.Count > 1);
        Assert.All(pages, page => Assert.InRange(page.Length, 1, 80));
        Assert.All(pages, page => Assert.DoesNotContain("… (truncated)", page));
        Assert.Contains("**Points leaderboard**", pages[0]);
        Assert.Contains("No points have been awarded yet.", Assert.Single(PointsBoardFormatter.FormatLeaderboard(Array.Empty<(ulong, int)>())));

        var ranks = new List<int>();
        foreach (var page in pages)
        {
            foreach (var line in page.Split('\n'))
            {
                if (line.Length == 0 || line.StartsWith("**", StringComparison.Ordinal))
                    continue;
                var dot = line.IndexOf('.');
                Assert.True(dot > 0, line);
                ranks.Add(int.Parse(line[..dot]));
                Assert.Contains(line, page);
            }
        }

        Assert.Equal(Enumerable.Range(1, 30), ranks);
        Assert.Contains("<@1> — 99", pages[0]);
        Assert.Contains("<@30> — 70", string.Join('\n', pages));
        Assert.DoesNotContain(pages, page => page.Contains("Showing", StringComparison.Ordinal));

        Assert.False(PointsBoardFormatter.LeaderboardGrowthMovesBossTable(1, 1, 2));
        Assert.False(PointsBoardFormatter.LeaderboardGrowthMovesBossTable(3, 1, 2));
        Assert.False(PointsBoardFormatter.LeaderboardGrowthMovesBossTable(0, 2, 0));
        Assert.True(PointsBoardFormatter.LeaderboardGrowthMovesBossTable(1, 3, 2));

        var raids = new List<EventDefinition>
        {
            new()
            {
                Name = "Short",
                Type = EventType.Raid,
                DisplayName = "Short Raid",
                Bosses =
                [
                    new BossDefinition { Name = "Alpha" },
                    new BossDefinition { Name = "Beta", Points = 40 },
                ],
            },
            new()
            {
                Name = "Other",
                Type = EventType.Raid,
                DisplayName = "Other Raid",
                Bosses = [new BossDefinition { Name = "Gamma" }],
            },
        };
        var messages = PointsBoardFormatter.FormatBossTable(raids, 10, maxLength: 120);
        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 120));
        var text = string.Join('\n', messages);
        Assert.Contains("Alpha — 10", text);
        Assert.Contains("Beta — 40 (override)", text);
        Assert.Contains("Gamma — 10", text);
        Assert.Contains("Default points per kill: 10", text);
        Assert.Contains(messages, message => message.Contains("**Short Raid**", StringComparison.Ordinal) && message.Contains("Alpha"));
    }

    [Fact]
    public async Task PointsChannelState_MigratesASingleLeaderboardMessageId()
    {
        var directory = Directory.CreateTempSubdirectory("df-points-channel").FullName;
        var path = Path.Combine(directory, "points-channel.json");
        await File.WriteAllTextAsync(path, """
            {
              "version": 1,
              "leaderboardMessageId": 42,
              "bossTableMessageIds": [7, 8]
            }
            """);

        var store = new PointsChannelStore(path);
        await store.LoadAsync();
        Assert.Equal(new ulong[] { 42 }, store.LeaderboardMessageIds);
        Assert.Equal(new ulong[] { 7, 8 }, store.BossTableMessageIds);

        store.LeaderboardMessageIds.Add(43);
        await store.SaveAsync();
        var saved = await File.ReadAllTextAsync(path);
        Assert.Contains("leaderboardMessageIds", saved);

        var reloaded = new PointsChannelStore(path);
        await reloaded.LoadAsync();
        Assert.Equal(new ulong[] { 42, 43 }, reloaded.LeaderboardMessageIds);
        Assert.Equal(new ulong[] { 7, 8 }, reloaded.BossTableMessageIds);

        await File.WriteAllTextAsync(path, """
            {
              "version": 2,
              "leaderboardMessageId": 99,
              "leaderboardMessageIds": [],
              "bossTableMessageIds": [1]
            }
            """);
        var emptyList = new PointsChannelStore(path);
        await emptyList.LoadAsync();
        Assert.Empty(emptyList.LeaderboardMessageIds);
        Assert.Equal(new ulong[] { 1 }, emptyList.BossTableMessageIds);

        Assert.Equal(new ulong[] { 5 }, PointsChannelStore.MigrateLeaderboardIds(5, null));
        Assert.Empty(PointsChannelStore.MigrateLeaderboardIds(5, Array.Empty<ulong>()));
        Assert.Equal(new ulong[] { 3, 4 }, PointsChannelStore.MigrateLeaderboardIds(9, new ulong[] { 0, 3, 4 }));
    }

    [Fact]
    public void OpenEventLimit_RejectsAtTheCap()
    {
        Assert.Null(OpenEventLimit.Rejection(9, 10));
        Assert.Equal("9/10 open events", OpenEventLimit.Counter(9, 10));
        var error = OpenEventLimit.Rejection(10, 10);
        Assert.NotNull(error);
        Assert.Contains("10/10 open events", error);
        Assert.Contains("/dfcompleteevent", error);
        Assert.Contains("/dfcancelevent", error);
    }

    [Fact]
    public void MessagePolicy_KeepsLegacyChannelMessages_AndSkipsThreadSystemLines()
    {
        var legacy = TestSupport.Event40();
        legacy.ChannelId = 10;
        legacy.ThreadId = 20;
        legacy.EmbedInThread = false;
        Assert.Equal(10UL, EventMessagePolicy.LinkChannelId(legacy));
        Assert.True(EventMessagePolicy.ReactionBelongsToEvent(legacy, 10, eventsChannelId: 10));
        Assert.False(EventMessagePolicy.ReactionBelongsToEvent(legacy, 20, eventsChannelId: 10));

        var threaded = TestSupport.Event40();
        threaded.ChannelId = 10;
        threaded.ThreadId = 20;
        threaded.EmbedInThread = true;
        Assert.Equal(20UL, EventMessagePolicy.LinkChannelId(threaded));
        Assert.True(EventMessagePolicy.ReactionBelongsToEvent(threaded, 20, eventsChannelId: 10));
        Assert.False(EventMessagePolicy.ReactionBelongsToEvent(threaded, 10, eventsChannelId: 10));

        Assert.False(EventMessagePolicy.DeleteParentMessage(true, isUserMessage: true, isThreadCreatedSystem: true, isBotAuthor: true));
        Assert.False(EventMessagePolicy.DeleteParentMessage(true, isUserMessage: false, isThreadCreatedSystem: false, isBotAuthor: true));
        Assert.True(EventMessagePolicy.DeleteParentMessage(true, isUserMessage: true, isThreadCreatedSystem: false, isBotAuthor: false));
        Assert.False(EventMessagePolicy.DeleteParentMessage(false, isUserMessage: true, isThreadCreatedSystem: false, isBotAuthor: false));
        Assert.True(EventMessagePolicy.DeleteThreadMessage(true, isEventThread: true, isUserMessage: true, isBotAuthor: false));
        Assert.False(EventMessagePolicy.DeleteThreadMessage(true, isEventThread: true, isUserMessage: true, isBotAuthor: true));
        Assert.False(EventMessagePolicy.DeleteThreadMessage(true, isEventThread: false, isUserMessage: true, isBotAuthor: false));
        Assert.False(EventMessagePolicy.DeleteThreadMessage(false, isEventThread: true, isUserMessage: true, isBotAuthor: false));
    }

    [Fact]
    public void Embed_ShowsPointsState_KilledBosses_Exclusions_AndCompleted()
    {
        var e = TestSupport.Event40();
        e.AwardsPoints = true;
        e.Kills.Add(new BossKillRecord { Boss = "Lucifron" });
        e.ExcludedUserIds.Add(42);
        var renderer = new EventRenderer(TestSupport.Config());
        var open = renderer.BuildEmbed(e);
        Assert.Contains("Points: enabled", open.Description);
        Assert.Contains("Bosses killed: Lucifron", open.Description);
        Assert.Contains("Excluded from future kills", open.Fields.Select(f => f.Name));
        Assert.Contains("<@42>", open.Fields.Single(f => f.Name.Contains("Excluded")).Value);

        e.Status = EventStatus.Completed;
        var completed = renderer.BuildEmbed(e);
        Assert.Contains("**Completed**", completed.Description);
        Assert.DoesNotContain("✅ full", completed.Description);

        e.AwardsPoints = false;
        e.Kills.Clear();
        e.Status = EventStatus.Open;
        Assert.Contains("Points: disabled", renderer.BuildEmbed(e).Description);
    }

    [Fact]
    public async Task Persistence_RoundTripsKillsSnapshotsExclusionsAndLedger_AndOldEventsStayCompatible()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var eventsPath = Path.Combine(dir, "events.json");
        var pointsPath = Path.Combine(dir, "points.json");

        var e = RaidWithRoster(full: true, bench: 1, cantAttend: 1);
        e.MessageId = 42;
        e.ThreadId = 77;
        e.ChannelId = 5;
        e.EmbedInThread = true;
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron"), T0).Ok);
        Assert.True(PointsLogic.Exclude(e, 3).Ok);
        e.Status = EventStatus.Completed;
        e.CompletedAt = T0;

        var store = new EventStore(eventsPath);
        store.Add(e);
        await store.SaveAsync();

        var entries = Pay(e);
        var points = new PointsStore(pointsPath);
        points.AddRange(entries);
        await points.SaveAsync();

        var reloaded = new EventStore(eventsPath);
        await reloaded.LoadAsync();
        var stored = Assert.Single(reloaded.Events);
        Assert.True(stored.AwardsPoints);
        Assert.True(stored.EmbedInThread);
        Assert.Equal(EventStatus.Completed, stored.Status);
        Assert.Equal(T0, stored.CompletedAt);
        Assert.Equal(new[] { 3UL }, stored.ExcludedUserIds);
        var kill = Assert.Single(stored.Kills);
        Assert.Equal("Lucifron", kill.Boss);
        Assert.True(kill.RaidWasFull);
        Assert.Equal(40, kill.Attendees.Count);
        Assert.Equal(500UL, Assert.Single(kill.Bench).UserId);
        Assert.Equal(PointsLogic.BenchRole, kill.Bench[0].Role);

        var reloadedPoints = new PointsStore(pointsPath);
        await reloadedPoints.LoadAsync();
        Assert.Equal(entries.Count, reloadedPoints.Entries.Count);
        Assert.True(reloadedPoints.HasEvent(42));
        Assert.Equal(PointsLogic.Total(entries, 1), PointsLogic.Total(reloadedPoints.Entries, 1));
        Assert.False(File.Exists(pointsPath + ".tmp"));

        var legacyPath = Path.Combine(dir, "legacy.json");
        await File.WriteAllTextAsync(legacyPath, """
            {
              "version": 1,
              "events": [
                {
                  "eventName": "MoltenCore40",
                  "displayName": "Molten Core",
                  "attendiesRequired": 40,
                  "attendyPerRole": { "Tank": 4 },
                  "startTime": "2026-10-24T16:00:00+00:00",
                  "durationInHours": 4,
                  "guildId": 1,
                  "channelId": 2,
                  "messageId": 3,
                  "threadId": 4,
                  "signups": [ { "userId": 5, "roleKey": "Tank", "signedUpAt": "2026-10-23T07:00:00+00:00" } ]
                }
              ]
            }
            """);
        var legacy = new EventStore(legacyPath);
        await legacy.LoadAsync();
        var old = Assert.Single(legacy.Events);
        Assert.False(old.AwardsPoints);
        Assert.False(old.EmbedInThread);
        Assert.Equal(EventStatus.Open, old.Status);
        Assert.Empty(old.Kills);
        Assert.Empty(old.ExcludedUserIds);
        Assert.Null(old.CompletedAt);
        Assert.Equal(5UL, Assert.Single(old.Signups).UserId);
        Assert.Equal(4UL, old.ThreadId);
    }

    [Fact]
    public void CompletedEvent_SecondPayoutDoesNotDuplicateEntries()
    {
        var e = RaidWithRoster(full: true, bench: 0, cantAttend: 0);
        Assert.True(PointsLogic.RecordKill(e, "Lucifron", Bosses("Lucifron"), T0).Ok);
        var first = PointsLogic.Complete(e, _ => 10, 25, T0, false);
        Assert.NotEmpty(first.Entries);
        var second = PointsLogic.Complete(e, _ => 10, 25, T0.AddMinutes(5), false);
        Assert.True(second.AlreadyCompleted);
        Assert.Empty(second.Entries);
    }

    private static List<PointsLedgerEntry> Pay(EventInstance e) =>
        PointsLogic.BuildEntries(e, _ => 10, 25, T0);

    private static List<BossDefinition> Bosses(params string[] names) =>
        names.Select(name => new BossDefinition { Name = name }).ToList();

    private static PointsLedgerEntry Entry(ulong userId, int points, string id) => new()
    {
        Id = id,
        UserId = userId,
        Points = points,
        Boss = "Lucifron",
        Role = "Tank",
        Timestamp = T0,
    };

    /// <summary>40 or 39 normal sign-ups, plus optional bench users at 500+ and one Cant attend at 900.</summary>
    private static EventInstance RaidWithRoster(bool full, int bench, int cantAttend)
    {
        var e = TestSupport.Event40();
        e.AwardsPoints = true;
        e.MessageId = 42;
        var count = full ? 40 : 39;
        for (ulong id = 1; id <= (ulong)count; id++)
            SignupLogic.Apply(e, id, id <= 4 ? "Tank" : "Range-DPS", T0.AddSeconds(id));
        for (var i = 0; i < bench; i++)
            SignupLogic.Apply(e, (ulong)(500 + i), "Bench", T0);
        if (cantAttend > 0)
            SignupLogic.Apply(e, 900, "Cant-attend", T0);
        return e;
    }
}
