using System.Net;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class ThreadDeletionTests
{
    [Fact]
    public async Task HandleThreadDeleted_RemovesTheEvent_AndLeavesOtherEvents()
    {
        await using var harness = Harness.Create();
        harness.Store.Add(Threaded("Molten Core", threadId: 50, messageId: 501));
        harness.Store.Add(Threaded("Onyxia", threadId: 60, messageId: 601));

        await harness.Events.HandleThreadDeletedAsync(50);

        Assert.Null(harness.Store.FindByThreadId(50));
        Assert.NotNull(harness.Store.FindByThreadId(60));
        var reloaded = new EventStore(harness.EventsPath);
        await reloaded.LoadAsync();
        Assert.Null(reloaded.FindByThreadId(50));
        Assert.NotNull(reloaded.FindByThreadId(60));
        Assert.Contains(harness.Log.Warnings, w =>
            w.Contains("Molten Core") && w.Contains("was deleted") && w.Contains("was removed") && w.Contains("(deleter unknown)"));
    }

    [Fact]
    public async Task HandleThreadDeleted_IgnoresAnUnknownThreadId()
    {
        await using var harness = Harness.Create();
        harness.Store.Add(Threaded("Molten Core", threadId: 50, messageId: 501));

        await harness.Events.HandleThreadDeletedAsync(999);
        await harness.Events.HandleThreadDeletedAsync(0);

        Assert.NotNull(harness.Store.FindByThreadId(50));
        Assert.Empty(harness.Log.Warnings);
        Assert.False(File.Exists(harness.EventsPath));
    }

    [Fact]
    public async Task DeletingTheStarterMessageAndTheThread_RemovesTheEventOnce()
    {
        await using var harness = Harness.Create();
        var raid = Threaded("Molten Core", threadId: 50, messageId: 50);
        harness.Store.Add(raid);

        await harness.Events.HandleMessageDeletedAsync(50, 50);
        await harness.Events.HandleThreadDeletedAsync(50);

        Assert.Empty(harness.Store.Events);
        var warning = Assert.Single(harness.Log.Warnings);
        Assert.Contains("was deleted", warning);
        Assert.Contains("was removed", warning);
        Assert.Contains("(deleter unknown)", warning);
    }

    [Fact]
    public async Task HandleThreadDeleted_KeepsLedgerEntries_ForACompletedEvent()
    {
        await using var harness = Harness.Create();
        var raid = Threaded("Molten Core", threadId: 50, messageId: 501);
        raid.Status = EventStatus.Completed;
        raid.AwardsPoints = true;
        raid.Kills.Add(new BossKillRecord { Boss = "Lucifron", KilledAt = TestSupport.Now, RaidWasFull = true });
        harness.Store.Add(raid);
        harness.Points.AddRange(new[]
        {
            new PointsLedgerEntry
            {
                Id = "paid-1",
                EventId = 501,
                EventName = "MoltenCore40",
                DisplayName = "Molten Core",
                Boss = "Lucifron",
                UserId = 7,
                Role = "Tank",
                Points = 10,
                Timestamp = TestSupport.Now,
            },
        });

        await harness.Events.HandleThreadDeletedAsync(50);

        Assert.Empty(harness.Store.Events);
        var entry = Assert.Single(harness.Points.Entries);
        Assert.Equal(10, entry.Points);
        Assert.Equal(7UL, entry.UserId);
        Assert.Equal("Lucifron", entry.Boss);
    }

    [Theory]
    [InlineData(DiscordObjectState.NotFound, DiscordObjectState.Unknown, true)]
    [InlineData(DiscordObjectState.NotFound, DiscordObjectState.Present, true)]
    [InlineData(DiscordObjectState.Present, DiscordObjectState.NotFound, true)]
    [InlineData(DiscordObjectState.Present, DiscordObjectState.Present, false)]
    [InlineData(DiscordObjectState.Present, DiscordObjectState.Unknown, false)]
    [InlineData(DiscordObjectState.Unknown, DiscordObjectState.NotFound, false)]
    [InlineData(DiscordObjectState.Unknown, DiscordObjectState.Unknown, false)]
    public void Reconcile_RemovesThreadedEvent_OnlyWhenThreadOrMessageIsDefinitelyNotFound(
        DiscordObjectState thread, DiscordObjectState message, bool remove)
    {
        var e = Threaded("Molten Core", threadId: 50, messageId: 501);
        Assert.Equal(remove, EventAbsence.ShouldRemoveAfterReconcile(e, thread, message));
    }

    [Fact]
    public void Reconcile_LeavesLegacyChannelMessages_ToTheMessageLookup()
    {
        var legacy = Threaded("Molten Core", threadId: 50, messageId: 501);
        legacy.EmbedInThread = false;
        Assert.False(EventAbsence.ShouldRemoveAfterReconcile(legacy, DiscordObjectState.NotFound, DiscordObjectState.NotFound));

        var missingThreadId = Threaded("Molten Core", threadId: 0, messageId: 501);
        Assert.False(EventAbsence.ShouldRemoveAfterReconcile(missingThreadId, DiscordObjectState.NotFound, DiscordObjectState.NotFound));
    }

    [Fact]
    public void LookupFailure_IsNotFoundOnlyForHttp404()
    {
        var request = new StubRequest();
        Assert.Equal(DiscordObjectState.NotFound, EventAbsence.FromException(
            new HttpException(HttpStatusCode.NotFound, request, DiscordErrorCode.UnknownChannel, "Unknown Channel")));
        Assert.Equal(DiscordObjectState.NotFound, EventAbsence.FromException(
            new HttpException(HttpStatusCode.NotFound, request, DiscordErrorCode.UnknownMessage, "Unknown Message")));
        Assert.Equal(DiscordObjectState.Unknown, EventAbsence.FromException(
            new HttpException(HttpStatusCode.Forbidden, request, DiscordErrorCode.MissingPermissions, "Missing Access")));
        Assert.Equal(DiscordObjectState.Unknown, EventAbsence.FromException(new RateLimitedException(request)));
        Assert.Equal(DiscordObjectState.Unknown, EventAbsence.FromException(new HttpRequestException("network")));
        Assert.Equal(DiscordObjectState.Unknown, EventAbsence.FromException(new TimeoutException("slow")));
    }

    private static EventInstance Threaded(string displayName, ulong threadId, ulong messageId) => new()
    {
        EventName = "MoltenCore40",
        DisplayName = displayName,
        AttendiesRequired = 40,
        StartTime = new DateTimeOffset(2026, 10, 24, 16, 0, 0, TimeSpan.Zero),
        DurationInHours = 4,
        ChannelId = 10,
        ThreadId = threadId,
        MessageId = messageId,
        EmbedInThread = true,
        Status = EventStatus.Open,
    };

    private sealed class StubRequest : IRequest
    {
        public DateTimeOffset? TimeoutAt => null;
        public RequestOptions Options => new();
    }

    private sealed class MemoryLog : IBotLog
    {
        public List<string> Warnings { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required EventStore Store { get; init; }
        public required PointsStore Points { get; init; }
        public required EventService Events { get; init; }
        public required MemoryLog Log { get; init; }
        public required string EventsPath { get; init; }
        public required DiscordSocketClient Client { get; init; }
        private readonly string _directory;

        private Harness(string directory) => _directory = directory;

        public static Harness Create()
        {
            var directory = Directory.CreateTempSubdirectory("df-thread-delete").FullName;
            var config = TestSupport.Config();
            var channels = new ResolvedChannels();
            var client = new DiscordSocketClient();
            var store = new EventStore(Path.Combine(directory, "events.json"));
            var points = new PointsStore(Path.Combine(directory, "points.json"));
            var definitions = new EventDefinitionRepository(config);
            var log = new MemoryLog();
            var board = new PointsBoardService(
                client, config, channels, points,
                new PointsChannelStore(Path.Combine(directory, "points-channel.json")),
                definitions, log);
            var events = new EventService(client, config, channels, store, points, definitions, new EventRenderer(config), log, board);
            return new Harness(directory)
            {
                Store = store,
                Points = points,
                Events = events,
                Log = log,
                EventsPath = Path.Combine(directory, "events.json"),
                Client = client,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            try { Directory.Delete(_directory, recursive: true); } catch { /* temp dir */ }
        }
    }
}
