namespace DungeonForge.Services;

/// <summary>
/// Matches a Discord audit-log row to a deleted event thread or sign-up message, and appends that person to the log line.
/// No Discord calls: the service reads the audit log and passes plain rows in.
/// </summary>
public static class DeletionAudit
{
    public const int AttemptCount = 3;

    /// <summary>Pause between audit-log reads. The entry often shows up a moment after the gateway event.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(1500);

    /// <summary>A row older than this is a different delete.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(15);

    /// <summary>
    /// A row dated slightly after the gateway event still counts.
    /// The audit timestamp and the bot clock are not the same instant.
    /// </summary>
    public static readonly TimeSpan FutureSkew = TimeSpan.FromSeconds(5);

    /// <summary>
    /// One audit row. ThreadId is set for a thread delete.
    /// ChannelId and MessageAuthorId are set for a message delete.
    /// MessageAuthorId is the author of the deleted messages, not the person who deleted them.
    /// </summary>
    public readonly record struct Entry(
        ulong ActorId,
        DateTimeOffset CreatedAt,
        ulong? ThreadId,
        ulong? ChannelId,
        ulong? MessageAuthorId);

    public static string RemovalLine(string noun, string displayName, string when) =>
        $"The {noun} of event **{displayName}** ({when}) was deleted, so the event was removed.";

    /// <summary>
    /// Keeps <paramref name="line"/> and appends the actor before the final period.
    /// A missing actor becomes <c>(deleter unknown)</c>.
    /// </summary>
    public static string WithActor(string line, ulong? actorId)
    {
        var attribution = actorId is ulong id && id != 0
            ? $" by {EventRenderer.Mention(id)}"
            : " (deleter unknown)";
        if (line.EndsWith('.'))
            return string.Concat(line.AsSpan(0, line.Length - 1), attribution, ".");
        return line + attribution;
    }

    /// <summary>
    /// The newest row inside the window. A thread id wins over a message match, so a forum post
    /// (starter message id equals the thread id) is attributed to the thread delete when both exist.
    /// Message rows must share the channel. When both sides have an author id, that author must be the bot.
    /// </summary>
    public static ulong? FindActor(
        IReadOnlyList<Entry> entries,
        ulong? threadId,
        ulong? channelId,
        ulong? messageAuthorId,
        DateTimeOffset now)
    {
        if (threadId is ulong thread)
        {
            var actor = Newest(entries, now, entry => entry.ThreadId == thread);
            if (actor is not null)
                return actor;
        }

        if (channelId is ulong channel)
            return Newest(entries, now, entry => MatchesMessage(entry, channel, messageAuthorId));

        return null;
    }

    private static bool MatchesMessage(Entry entry, ulong channelId, ulong? messageAuthorId)
    {
        if (entry.ChannelId != channelId)
            return false;
        if (messageAuthorId is ulong expected && entry.MessageAuthorId is ulong actual)
            return actual == expected;
        return true;
    }

    private static ulong? Newest(IReadOnlyList<Entry> entries, DateTimeOffset now, Func<Entry, bool> matches)
    {
        var earliest = now - Window;
        var latest = now + FutureSkew;
        ulong? actor = null;
        var best = DateTimeOffset.MinValue;
        foreach (var entry in entries)
        {
            if (entry.ActorId == 0 || !matches(entry))
                continue;
            if (entry.CreatedAt < earliest || entry.CreatedAt > latest)
                continue;
            if (actor is not null && entry.CreatedAt <= best)
                continue;
            actor = entry.ActorId;
            best = entry.CreatedAt;
        }

        return actor;
    }
}
