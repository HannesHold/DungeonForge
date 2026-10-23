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
    public bool LockEventThreads { get; set; }
    public bool LogToDiscord { get; set; } = true;
    public string DataDirectory { get; set; } = "Data";
    public int FinishedEventRetentionHours { get; set; } = 24;
}

public sealed class ChannelsConfig
{
    public NamedId Events { get; set; } = new() { Name = "dungeon-forge" };
    public NamedId Manage { get; set; } = new() { Name = "dungeon-forge-manage" };
    public NamedId Log { get; set; } = new() { Name = "dungeon-forge-log" };
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
