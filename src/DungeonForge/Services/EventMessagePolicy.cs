using DungeonForge.Models;

namespace DungeonForge.Services;

/// <summary>Where an event embed lives, and which stray messages the bot may delete. No Discord calls.</summary>
public static class EventMessagePolicy
{
    /// <summary>Channel id used in a Discord message link. Thread messages link to the thread.</summary>
    public static ulong LinkChannelId(EventInstance e) =>
        e.EmbedInThread && e.ThreadId != 0 ? e.ThreadId : e.ChannelId;

    /// <summary>True when a reaction in <paramref name="channelId"/> belongs to this event's embed message.</summary>
    public static bool ReactionBelongsToEvent(EventInstance e, ulong channelId, ulong eventsChannelId)
    {
        if (e.EmbedInThread)
            return e.ThreadId != 0 && channelId == e.ThreadId;

        return channelId == eventsChannelId || (e.ChannelId != 0 && channelId == e.ChannelId);
    }

    /// <summary>
    /// Top-level messages in the events channel. Thread-created system lines and the bot's own messages stay.
    /// </summary>
    public static bool DeleteParentMessage(bool deleteOption, bool isUserMessage, bool isThreadCreatedSystem, bool isBotAuthor)
    {
        if (!deleteOption || isThreadCreatedSystem || isBotAuthor || !isUserMessage)
            return false;
        return true;
    }

    /// <summary>Non-bot user messages inside an event thread, when the option is on.</summary>
    public static bool DeleteThreadMessage(bool deleteOption, bool isEventThread, bool isUserMessage, bool isBotAuthor) =>
        deleteOption && isEventThread && isUserMessage && !isBotAuthor;
}
