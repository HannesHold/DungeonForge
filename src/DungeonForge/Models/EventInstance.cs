namespace DungeonForge.Models;

/// <summary>A concrete scheduled event posted in Discord. Persisted to Data/events.json.</summary>
public sealed class EventInstance
{
    public string EventName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int AttendiesRequired { get; set; }
    public Dictionary<string, int> AttendyPerRole { get; set; } = new();
    public DateTimeOffset StartTime { get; set; }
    public double DurationInHours { get; set; }

    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }
    public ulong ThreadId { get; set; }
    public ulong CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Signup> Signups { get; set; } = new();

    /// <summary>True when boss kills on this event pay points. Dungeons never award points. Missing in old state files means false.</summary>
    public bool AwardsPoints { get; set; }

    /// <summary>Missing in old state files means <see cref="EventStatus.Open"/>.</summary>
    public EventStatus Status { get; set; } = EventStatus.Open;

    /// <summary>Boss kills in the order they were recorded, each with the roster at that moment.</summary>
    public List<BossKillRecord> Kills { get; set; } = new();

    /// <summary>Users left out of future kill snapshots even if they are still signed up.</summary>
    public List<ulong> ExcludedUserIds { get; set; } = new();

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// True when the embed message lives inside <see cref="ThreadId"/>.
    /// Missing or false means the older layout: the embed is a message in <see cref="ChannelId"/> with a thread attached.
    /// </summary>
    public bool EmbedInThread { get; set; }

    public DateTimeOffset EndTime => StartTime.AddHours(DurationInHours);
}

public sealed class Signup
{
    public ulong UserId { get; set; }
    public string RoleKey { get; set; } = "";
    public DateTimeOffset SignedUpAt { get; set; }
}
