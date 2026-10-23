using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IEventStore
{
    IReadOnlyList<EventInstance> Events { get; }
    EventInstance? FindByMessageId(ulong messageId);
    EventInstance? FindByThreadId(ulong threadId);
    void Add(EventInstance e);
    bool Remove(EventInstance e);
    Task LoadAsync();
    Task SaveAsync();
}

/// <summary>
/// In-memory list of active events persisted to Data/events.json (atomic write).
/// Callers serialize access (see EventService); the store itself only guards the file write.
/// </summary>
public sealed class EventStore : IEventStore
{
    private sealed class StateFile
    {
        public int Version { get; set; } = 1;
        public List<EventInstance> Events { get; set; } = new();
    }

    private readonly string _path;
    private readonly ILogger<EventStore>? _logger;
    private readonly List<EventInstance> _events = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public EventStore(BotConfiguration config, ILogger<EventStore>? logger = null)
        : this(Path.Combine(config.DataDirectory, "events.json"), logger) { }

    public EventStore(string path, ILogger<EventStore>? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    public IReadOnlyList<EventInstance> Events => _events;

    public EventInstance? FindByMessageId(ulong messageId) => _events.FirstOrDefault(e => e.MessageId == messageId);

    public EventInstance? FindByThreadId(ulong threadId) =>
        threadId == 0 ? null : _events.FirstOrDefault(e => e.ThreadId == threadId);

    public void Add(EventInstance e) => _events.Add(e);

    public bool Remove(EventInstance e) => _events.Remove(e);

    public async Task LoadAsync()
    {
        _events.Clear();
        if (!File.Exists(_path)) return;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<StateFile>(stream, JsonDefaults.State);
            if (state?.Events is null)
                return;

            foreach (var e in state.Events)
            {
                if (e is null)
                    continue;
                Normalize(e);
                _events.Add(e);
            }
        }
        catch (JsonException ex)
        {
            // Keep the broken file for inspection instead of silently overwriting it.
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(_path, backup, overwrite: true);
            _logger?.LogError(ex, "State file {Path} is corrupt; a copy was saved as {Backup}. Starting with no events.", _path, backup);
        }
    }

    public async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(new StateFile { Events = _events.ToList() }, JsonDefaults.State);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>Old events.json files omit the points and thread-layout fields. Fill those in so the rest of the bot can assume non-null lists.</summary>
    private static void Normalize(EventInstance e)
    {
        e.Signups ??= new List<Signup>();
        e.AttendyPerRole ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        e.Kills ??= new List<BossKillRecord>();
        e.ExcludedUserIds ??= new List<ulong>();
        e.Kills.RemoveAll(kill => kill is null);
        foreach (var kill in e.Kills)
        {
            kill.Boss ??= "";
            kill.Attendees ??= new List<KillAttendee>();
            kill.Bench ??= new List<KillAttendee>();
            kill.Attendees.RemoveAll(attendee => attendee is null);
            kill.Bench.RemoveAll(attendee => attendee is null);
        }
    }
}
