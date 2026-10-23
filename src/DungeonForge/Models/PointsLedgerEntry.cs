namespace DungeonForge.Models;

/// <summary>One append-only points grant. Totals are the sum of a user's entries.</summary>
public sealed class PointsLedgerEntry
{
    public string Id { get; set; } = "";
    public ulong EventId { get; set; }
    public string EventName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Boss { get; set; } = "";
    public ulong UserId { get; set; }

    /// <summary>Role key at that kill, or <c>Bench</c>.</summary>
    public string Role { get; set; } = "";

    public int Points { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
