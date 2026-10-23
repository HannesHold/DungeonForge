using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IEventStore
{
    IReadOnlyList<EventInstance> Events { get; }
    EventInstance? FindByMessageId(ulong messageId);
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
            if (state is not null) _events.AddRange(state.Events);
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
}
