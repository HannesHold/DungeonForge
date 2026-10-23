using System.Text.Json;
using DungeonForge.Configuration;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IPointsChannelStore
{
    ulong LeaderboardMessageId { get; set; }
    List<ulong> BossTableMessageIds { get; }
    Task LoadAsync();
    Task SaveAsync();
}

/// <summary>Message ids of the points-channel leaderboard and boss table, so a restart edits them instead of posting again.</summary>
public sealed class PointsChannelStore : IPointsChannelStore
{
    private sealed class StateFile
    {
        public int Version { get; set; } = 1;
        public ulong LeaderboardMessageId { get; set; }
        public List<ulong> BossTableMessageIds { get; set; } = new();
    }

    private readonly string _path;
    private readonly ILogger<PointsChannelStore>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private ulong _leaderboardMessageId;
    private List<ulong> _bossTableMessageIds = new();

    public PointsChannelStore(BotConfiguration config, ILogger<PointsChannelStore>? logger = null)
        : this(Path.Combine(config.DataDirectory, "points-channel.json"), logger) { }

    public PointsChannelStore(string path, ILogger<PointsChannelStore>? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    public ulong LeaderboardMessageId
    {
        get => _leaderboardMessageId;
        set => _leaderboardMessageId = value;
    }

    public List<ulong> BossTableMessageIds => _bossTableMessageIds;

    public async Task LoadAsync()
    {
        _leaderboardMessageId = 0;
        _bossTableMessageIds = new List<ulong>();
        if (!File.Exists(_path))
            return;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<StateFile>(stream, JsonDefaults.State);
            if (state is null)
                return;
            _leaderboardMessageId = state.LeaderboardMessageId;
            _bossTableMessageIds = state.BossTableMessageIds ?? new List<ulong>();
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(_path, backup, overwrite: true);
            _logger?.LogError(ex, "Points channel file {Path} is corrupt; a copy was saved as {Backup}.", _path, backup);
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

            var json = JsonSerializer.Serialize(new StateFile
            {
                LeaderboardMessageId = _leaderboardMessageId,
                BossTableMessageIds = _bossTableMessageIds.ToList(),
            }, JsonDefaults.State);
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
