namespace DungeonForge.Models;

/// <summary>One recorded boss kill and the roster that was present when it was recorded.</summary>
public sealed class BossKillRecord
{
    public string Boss { get; set; } = "";
    public DateTimeOffset KilledAt { get; set; }

    /// <summary>True when normal-role sign-ups were at least <see cref="EventInstance.AttendiesRequired"/>.</summary>
    public bool RaidWasFull { get; set; }

    /// <summary>Players signed up with a normal role, excluding anyone on the event's exclusion list.</summary>
    public List<KillAttendee> Attendees { get; set; } = new();

    /// <summary>Players signed up as Bench, excluding anyone on the event's exclusion list.</summary>
    public List<KillAttendee> Bench { get; set; } = new();
}

/// <summary>A player captured on a boss-kill snapshot.</summary>
public sealed class KillAttendee
{
    public ulong UserId { get; set; }

    /// <summary>Role key (Tank, Heal, …) or <c>Bench</c>.</summary>
    public string Role { get; set; } = "";
}
