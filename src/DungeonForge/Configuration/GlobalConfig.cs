namespace DungeonForge.Configuration;

/// <summary>Mapping of Configurations/global.json.</summary>
public sealed class GlobalConfig
{
    public const string TokenPlaceholder = "YOUR_BOT_TOKEN";

    public string Token { get; set; } = TokenPlaceholder;
    public ulong GuildId { get; set; }
    public string TimeZone { get; set; } = "Europe/Vienna";
    public ChannelsConfig Channels { get; set; } = new();
    public DiscordRolesConfig Roles { get; set; } = new();
    public bool DeleteStrayMessages { get; set; } = true;

    /// <summary>Delete non-bot messages posted inside event threads. Missing means on.</summary>
    public bool DeleteStrayThreadMessages { get; set; } = true;

    public bool LockEventThreads { get; set; }
    public bool LogToDiscord { get; set; } = true;
    public string DataDirectory { get; set; } = "Data";
    public int FinishedEventRetentionHours { get; set; } = 24;

    /// <summary>Points granted for a boss kill when the template does not override that boss. Must be &gt; 0.</summary>
    public int PointsPerBossKill { get; set; } = 10;

    /// <summary>Percentage of boss points a benched player receives when the raid was full at that kill. 0–100.</summary>
    public int BenchPointsPercent { get; set; } = 25;

    /// <summary>Archive and lock the event thread when an event is completed. Missing means on.</summary>
    public bool ArchiveCompletedEventThreads { get; set; } = true;

    /// <summary>Maximum number of events with status Open at once. Completed events do not count.</summary>
    public int MaxOpenEvents { get; set; } = 10;
}

public sealed class ChannelsConfig
{
    /// <summary>Default name of the events channel. It may be a text channel or a forum channel.</summary>
    public const string DefaultEventsName = "dungeon-forge-forum";

    public NamedId Events { get; set; } = new() { Name = DefaultEventsName };
    public NamedId Manage { get; set; } = new() { Name = "dungeon-forge-manage" };
    public NamedId Log { get; set; } = new() { Name = "dungeon-forge-log" };

    /// <summary>Leaderboard and raid boss-point table. Id 0 looks the channel up by name. A missing channel does not stop the bot.</summary>
    public NamedId Points { get; set; } = new() { Name = "dungeon-forge-points" };
}

public sealed class DiscordRolesConfig
{
    public NamedId Dungeoneer { get; set; } = new() { Name = "df-dungeoneer" };
    public NamedId Manager { get; set; } = new() { Name = "df-manager" };
    public NamedId Logs { get; set; } = new() { Name = "df-logs" };
}

/// <summary>A Discord entity (channel / role) referenced by id and/or name. Id 0 means "look up by name".</summary>
public sealed class NamedId
{
    public string Name { get; set; } = "";
    public ulong Id { get; set; }

    public override string ToString() => Id != 0 ? $"{Name} ({Id})" : Name;
}
