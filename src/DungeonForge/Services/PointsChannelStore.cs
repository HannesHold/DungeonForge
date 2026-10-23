using System.Text.Json;
using DungeonForge.Configuration;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IPointsChannelStore
{
    List<ulong> LeaderboardMessageIds { get; }
    List<ulong> BossTableMessageIds { get; }
    Task LoadAsync();
    Task SaveAsync();
}

/// <summary>
/// Message ids of the points-channel leaderboard and boss table, so a restart edits them instead of posting again.
/// Older files stored one <c>leaderboardMessageId</c>; that id becomes the first entry of <see cref="LeaderboardMessageIds"/>.
/// </summary>
public sealed class PointsChannelStore : IPointsChannelStore
{
    private sealed class StateFile
    {
        public int Version { get; set; } = 2;
        public ulong LeaderboardMessageId { get; set; }
        public List<ulong>? LeaderboardMessageIds { get; set; }
        public List<ulong> BossTableMessageIds { get; set; } = new();
    }

    private readonly string _path;
    private readonly ILogger<PointsChannelStore>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private List<ulong> _leaderboardMessageIds = new();
    private List<ulong> _bossTableMessageIds = new();

    public PointsChannelStore(BotConfiguration config, ILogger<PointsChannelStore>? logger = null)
        : this(Path.Combine(config.DataDirectory, "points-channel.json"), logger) { }

    public PointsChannelStore(string path, ILogger<PointsChannelStore>? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    public List<ulong> LeaderboardMessageIds => _leaderboardMessageIds;

    public List<ulong> BossTableMessageIds => _bossTableMessageIds;

    /// <summary>
    /// A present <paramref name="leaderboardMessageIds"/> list wins, including an empty list.
    /// A file from before the list existed contributes its single <paramref name="legacyLeaderboardMessageId"/>.
    /// </summary>
    public static List<ulong> MigrateLeaderboardIds(ulong legacyLeaderboardMessageId, IReadOnlyList<ulong>? leaderboardMessageIds)
    {
        if (leaderboardMessageIds is not null)
            return leaderboardMessageIds.Where(id => id != 0).ToList();

        return legacyLeaderboardMessageId == 0
            ? new List<ulong>()
            : new List<ulong> { legacyLeaderboardMessageId };
    }

    public async Task LoadAsync()
    {
        _leaderboardMessageIds = new List<ulong>();
        _bossTableMessageIds = new List<ulong>();
        if (!File.Exists(_path))
            return;

        try
        {
            await using var stream = File.OpenRead(_path);
            var state = await JsonSerializer.DeserializeAsync<StateFile>(stream, JsonDefaults.State);
            if (state is null)
                return;

            _leaderboardMessageIds = MigrateLeaderboardIds(state.LeaderboardMessageId, state.LeaderboardMessageIds);
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
                LeaderboardMessageIds = _leaderboardMessageIds.ToList(),
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
