using System.Text.Json;
using DungeonForge.Configuration;
using DungeonForge.Models;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IPointsStore
{
    IReadOnlyList<PointsLedgerEntry> Entries { get; }
    bool HasEvent(ulong eventId);
    void AddRange(IEnumerable<PointsLedgerEntry> entries);
    Task LoadAsync();
    Task SaveAsync();
}

/// <summary>Append-only points ledger in Data/points.json. The file is rewritten atomically, like the event store.</summary>
public sealed class PointsStore : IPointsStore
{
    private sealed class StateFile
    {
        public int Version { get; set; } = 1;
        public List<PointsLedgerEntry> Entries { get; set; } = new();
    }

    private readonly string _path;
    private readonly ILogger<PointsStore>? _logger;
    private readonly List<PointsLedgerEntry> _entries = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public PointsStore(BotConfiguration config, ILogger<PointsStore>? logger = null)
        : this(Path.Combine(config.DataDirectory, "points.json"), logger) { }

    public PointsStore(string path, ILogger<PointsStore>? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    public IReadOnlyList<PointsLedgerEntry> Entries => _entries;

    public bool HasEvent(ulong eventId) => _entries.Any(entry => entry.EventId == eventId);

    public void AddRange(IEnumerable<PointsLedgerEntry> entries) => _entries.AddRange(entries);

    public async Task LoadAsync()
    {
        _entries.Clear();
        if (!File.Exists(_path))
            return;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<StateFile>(stream, JsonDefaults.State);
            if (state?.Entries is null)
                return;
            _entries.AddRange(state.Entries.Where(entry => entry is not null));
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(_path, backup, overwrite: true);
            _logger?.LogError(ex, "Points file {Path} is corrupt; a copy was saved as {Backup}. Starting with no points.", _path, backup);
        }
    }

    public async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(new StateFile { Entries = _entries.ToList() }, JsonDefaults.State);
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
