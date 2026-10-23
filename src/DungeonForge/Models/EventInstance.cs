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

    public DateTimeOffset EndTime => StartTime.AddHours(DurationInHours);
}

public sealed class Signup
{
    public ulong UserId { get; set; }
    public string RoleKey { get; set; } = "";
    public DateTimeOffset SignedUpAt { get; set; }
}
