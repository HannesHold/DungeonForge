using DungeonForge.Bot;
using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>
/// Result of looking up a thread or message on Discord.
/// <see cref="Unknown"/> is a transient failure (network, rate limit, missing permission) and must not drop stored events.
/// </summary>
public enum DiscordObjectState
{
    Present,
    NotFound,
    Unknown,
}

/// <summary>When startup reconcile may drop a stored event. No Discord calls.</summary>
public static class EventAbsence
{
    /// <summary>
    /// A threaded event is removed only when its thread, or the sign-up message inside a thread that is still there,
    /// is a definite not-found (HTTP 404 or a null message). Any unknown lookup keeps the event.
    /// Events on the older channel-message layout are decided by that message lookup, not here.
    /// </summary>
    public static bool ShouldRemoveAfterReconcile(EventInstance e, DiscordObjectState thread, DiscordObjectState message)
    {
        if (!e.EmbedInThread || e.ThreadId == 0)
            return false;
        if (thread == DiscordObjectState.NotFound)
            return true;
        if (thread != DiscordObjectState.Present)
            return false;
        return message == DiscordObjectState.NotFound;
    }

    /// <summary>
    /// HTTP 404 is a definite miss (Discord's Unknown Channel and Unknown Message).
    /// Rate limits, missing permissions, and network failures stay <see cref="DiscordObjectState.Unknown"/>.
    /// </summary>
    public static DiscordObjectState FromException(Exception ex) =>
        DiscordHelpers.IsNotFound(ex) ? DiscordObjectState.NotFound : DiscordObjectState.Unknown;
}
