namespace DungeonForge.Models;

/// <summary>Lifecycle of a scheduled event. Cancelled events are removed from the state file.</summary>
public enum EventStatus
{
    Open,
    Completed,
}
