using Discord;
using DungeonForge.Configuration;

namespace DungeonForge.Services;

public enum EventsChannelKind
{
    Text,
    Forum,
}

public readonly record struct EventsChannelCandidate(ulong Id, string Name, ChannelType Type);

public readonly record struct EventsChannelMatch(bool Found, ulong Id, string Name, EventsChannelKind Kind, string? Error);

public readonly record struct ForumTagOption(ulong Id, string Name);

/// <summary>Which forum tag to send with a new post. <see cref="Error"/> is set when a required tag cannot be chosen.</summary>
public readonly record struct ForumTagSelection(bool ApplyTag, ulong TagId, string TagName, string? Error);

public readonly record struct EventsChannelPermissionSnapshot(
    bool ViewChannel,
    bool SendMessages,
    bool CreatePublicThreads,
    bool SendMessagesInThreads,
    bool ManageThreads,
    bool ManageMessages,
    bool AddReactions,
    bool ReadMessageHistory,
    bool EmbedLinks);

/// <summary>Events-channel lookup, forum tags, and the permission list. No Discord calls.</summary>
public static class EventsChannelSupport
{
    public const string DefaultName = ChannelsConfig.DefaultEventsName;
    public const string PreviousDefaultName = "dungeon-forge";

    public static bool IsDefaultName(string? name) =>
        string.Equals(name?.Trim(), DefaultName, StringComparison.OrdinalIgnoreCase);

    public static EventsChannelKind? KindOf(ChannelType type) => type switch
    {
        ChannelType.Text => EventsChannelKind.Text,
        ChannelType.Forum => EventsChannelKind.Forum,
        _ => null,
    };

    public static string KindLabel(EventsChannelKind kind) =>
        kind == EventsChannelKind.Forum ? "forum" : "text";

    /// <summary>
    /// Id wins over name. A non-zero id must be a text or forum channel.
    /// A name matches one text or forum channel. Two matches ask for an id.
    /// </summary>
    public static EventsChannelMatch Resolve(IReadOnlyList<EventsChannelCandidate> channels, ulong configuredId, string? configuredName)
    {
        if (configuredId != 0)
        {
            var byId = channels.FirstOrDefault(c => c.Id == configuredId);
            if (byId.Id == 0)
                return Missing($"channels.events: no channel with id {configuredId} is visible to the bot.");

            var kind = KindOf(byId.Type);
            if (kind is null)
                return Missing($"channels.events: channel {configuredId} ({byId.Name}) is a {byId.Type} channel. Use a text channel or a forum channel.");

            return Found(byId, kind.Value);
        }

        var name = configuredName?.Trim() ?? "";
        var matches = channels.Where(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && KindOf(c.Type) is not null).ToList();
        if (matches.Count == 0)
            return Missing(MissingNameMessage(name));
        if (matches.Count > 1)
            return Missing($"channels.events: more than one text or forum channel is named '{name}'. Set channels.events.id in global.json.");

        return Found(matches[0], KindOf(matches[0].Type)!.Value);
    }

    public static string MissingNameMessage(string name)
    {
        if (IsDefaultName(name))
            return $"channels.events: no text or forum channel named '{DefaultName}' was found. " +
                   $"The default events channel name is {DefaultName} (previously {PreviousDefaultName}). " +
                   $"Rename your channel to {DefaultName}, or set channels.events.name or channels.events.id in global.json.";

        return $"channels.events: no text or forum channel named '{name}' was found (set its \"id\" in global.json).";
    }

    public static bool RequiresTag(ChannelFlags flags) => (flags & ChannelFlags.RequireTag) == ChannelFlags.RequireTag;

    /// <summary>When the forum requires a tag, the first tag in list order is applied. An empty list is an error.</summary>
    public static ForumTagSelection SelectTag(bool requireTag, IReadOnlyList<ForumTagOption> tags)
    {
        if (!requireTag)
            return new ForumTagSelection(false, 0, "", null);

        var tag = tags.FirstOrDefault(t => t.Id != 0 && !string.IsNullOrWhiteSpace(t.Name));
        if (tag.Id == 0)
            return new ForumTagSelection(false, 0, "",
                "This forum requires a tag on every post, and it has no tags. Add a tag in the forum settings, or turn off \"Require tag\", then try again.");

        return new ForumTagSelection(true, tag.Id, tag.Name, null);
    }

    /// <summary>
    /// Permissions the bot must have on the events channel.
    /// A forum uses Create Posts (Send Messages) and Send Messages in Posts.
    /// Its overwrite screen has no Create Public Threads toggle, and the effective channel permission for that bit
    /// can be false even when the role has it on the server, so a forum never reports it missing.
    /// A text channel still requires Create Public Threads for its standalone thread.
    /// </summary>
    public static IReadOnlyList<string> MissingPermissions(EventsChannelPermissionSnapshot granted, EventsChannelKind kind)
    {
        var forum = kind == EventsChannelKind.Forum;
        var missing = new List<string>();
        void Need(bool ok, string name)
        {
            if (!ok)
                missing.Add(name);
        }

        Need(granted.ViewChannel, "View Channel");
        Need(granted.SendMessages, forum ? "Create Posts" : "Send Messages");
        if (!forum)
            Need(granted.CreatePublicThreads, "Create Public Threads");
        Need(granted.SendMessagesInThreads, forum ? "Send Messages in Posts" : "Send Messages in Threads");
        Need(granted.ManageThreads, "Manage Threads");
        Need(granted.ManageMessages, "Manage Messages");
        Need(granted.AddReactions, "Add Reactions");
        Need(granted.ReadMessageHistory, "Read Message History");
        Need(granted.EmbedLinks, "Embed Links");
        return missing;
    }

    public static string StartupMessage(string channelName, EventsChannelKind kind, IReadOnlyList<string> missing)
    {
        string where;
        string needed;
        if (kind == EventsChannelKind.Forum)
        {
            where = $"Events channel **{channelName}** is a forum channel. Each event is a forum post. Creating a post needs Create Posts.";
            needed = " The bot needs View Channel, Create Posts, Send Messages in Posts, Manage Threads, Manage Messages, Add Reactions, Read Message History, and Embed Links.";
        }
        else
        {
            where = $"Events channel **{channelName}** is a text channel. Each event is a public thread in that channel (Create Public Threads).";
            needed = " The bot needs View Channel, Send Messages, Create Public Threads, Send Messages in Threads, Manage Threads, Manage Messages, Add Reactions, Read Message History, and Embed Links.";
        }

        if (missing.Count == 0)
            return where + needed;

        return where + needed + " Missing right now: " + string.Join(", ", missing) + ".";
    }

    private static EventsChannelMatch Found(EventsChannelCandidate channel, EventsChannelKind kind) =>
        new(true, channel.Id, channel.Name, kind, null);

    private static EventsChannelMatch Missing(string error) =>
        new(false, 0, "", default, error);
}
