using Discord;
using DungeonForge.Services;

namespace DungeonForge.Tests;

public class EventsChannelTests
{
    [Theory]
    [InlineData(ChannelType.Text, EventsChannelKind.Text)]
    [InlineData(ChannelType.Forum, EventsChannelKind.Forum)]
    public void KindOf_AcceptsTextAndForum(ChannelType type, EventsChannelKind kind) =>
        Assert.Equal(kind, EventsChannelSupport.KindOf(type));

    [Theory]
    [InlineData(ChannelType.Voice)]
    [InlineData(ChannelType.Category)]
    [InlineData(ChannelType.News)]
    [InlineData(ChannelType.PublicThread)]
    public void KindOf_RejectsOtherChannelTypes(ChannelType type) =>
        Assert.Null(EventsChannelSupport.KindOf(type));

    [Fact]
    public void Resolve_FindsAForumOrTextChannelByIdOrName()
    {
        var channels = new[]
        {
            new EventsChannelCandidate(10, "dungeon-forge-forum", ChannelType.Forum),
            new EventsChannelCandidate(11, "chat", ChannelType.Text),
            new EventsChannelCandidate(12, "voice", ChannelType.Voice),
        };

        var byForumId = EventsChannelSupport.Resolve(channels, 10, "ignored");
        Assert.True(byForumId.Found);
        Assert.Equal(EventsChannelKind.Forum, byForumId.Kind);
        Assert.Equal("dungeon-forge-forum", byForumId.Name);

        var byTextName = EventsChannelSupport.Resolve(channels, 0, "Chat");
        Assert.True(byTextName.Found);
        Assert.Equal(11UL, byTextName.Id);
        Assert.Equal(EventsChannelKind.Text, byTextName.Kind);

        var byForumName = EventsChannelSupport.Resolve(channels, 0, "DUNGEON-FORGE-FORUM");
        Assert.True(byForumName.Found);
        Assert.Equal(10UL, byForumName.Id);
        Assert.Equal(EventsChannelKind.Forum, byForumName.Kind);
    }

    [Fact]
    public void Resolve_RejectsANonTextNonForumId_AndDuplicateNames()
    {
        var voice = EventsChannelSupport.Resolve(
            new[] { new EventsChannelCandidate(5, "events", ChannelType.Voice) }, 5, "events");
        Assert.False(voice.Found);
        Assert.Contains("text channel or a forum", voice.Error);

        var missing = EventsChannelSupport.Resolve(Array.Empty<EventsChannelCandidate>(), 9, "dungeon-forge-forum");
        Assert.False(missing.Found);
        Assert.Contains("id 9", missing.Error);

        var duplicates = EventsChannelSupport.Resolve(
            new[]
            {
                new EventsChannelCandidate(1, "events", ChannelType.Text),
                new EventsChannelCandidate(2, "events", ChannelType.Forum),
            },
            0,
            "events");
        Assert.False(duplicates.Found);
        Assert.Contains("channels.events.id", duplicates.Error);
    }

    [Fact]
    public void Resolve_MissingDefaultName_MentionsTheNewDefault()
    {
        var match = EventsChannelSupport.Resolve(Array.Empty<EventsChannelCandidate>(), 0, EventsChannelSupport.DefaultName);
        Assert.False(match.Found);
        Assert.Equal(EventsChannelSupport.DefaultName, "dungeon-forge-forum");
        Assert.Contains("dungeon-forge-forum", match.Error);
        Assert.Contains("dungeon-forge", match.Error);
        Assert.Contains("global.json", match.Error);

        var custom = EventsChannelSupport.Resolve(Array.Empty<EventsChannelCandidate>(), 0, "my-events");
        Assert.Contains("my-events", custom.Error);
        Assert.DoesNotContain("previously", custom.Error);
    }

    [Fact]
    public void SelectTag_UsesTheFirstTagOnlyWhenTheForumRequiresOne()
    {
        var tags = new[]
        {
            new ForumTagOption(4, "Raid"),
            new ForumTagOption(5, "Dungeon"),
        };

        var optional = EventsChannelSupport.SelectTag(false, tags);
        Assert.False(optional.ApplyTag);
        Assert.Null(optional.Error);

        var required = EventsChannelSupport.SelectTag(true, tags);
        Assert.True(required.ApplyTag);
        Assert.Equal(4UL, required.TagId);
        Assert.Equal("Raid", required.TagName);
        Assert.Null(required.Error);

        var empty = EventsChannelSupport.SelectTag(true, Array.Empty<ForumTagOption>());
        Assert.False(empty.ApplyTag);
        Assert.Contains("no tags", empty.Error);
    }

    [Fact]
    public void RequiresTag_ReadsTheForumFlag()
    {
        Assert.False(EventsChannelSupport.RequiresTag(ChannelFlags.None));
        Assert.True(EventsChannelSupport.RequiresTag(ChannelFlags.RequireTag));
        Assert.True(EventsChannelSupport.RequiresTag(ChannelFlags.RequireTag | ChannelFlags.Pinned));
    }

    [Fact]
    public void StartupMessage_NamesTheKindAndAnyMissingPermissions()
    {
        var granted = new EventsChannelPermissionSnapshot(true, true, true, true, true, true, true, true, true);
        Assert.Empty(EventsChannelSupport.MissingPermissions(granted));

        var missingSend = granted with { SendMessages = false, EmbedLinks = false };
        var missing = EventsChannelSupport.MissingPermissions(missingSend);
        Assert.Equal(new[] { "Send Messages", "Embed Links" }, missing);

        var forum = EventsChannelSupport.StartupMessage("dungeon-forge-forum", EventsChannelKind.Forum, missing);
        Assert.Contains("forum", forum);
        Assert.Contains("dungeon-forge-forum", forum);
        Assert.Contains("Send Messages", forum);
        Assert.Contains("Create Posts", forum);
        Assert.Contains("Missing right now: Send Messages, Embed Links.", forum);

        var text = EventsChannelSupport.StartupMessage("events", EventsChannelKind.Text, Array.Empty<string>());
        Assert.Contains("text channel", text);
        Assert.Contains("Create Public Threads", text);
        Assert.DoesNotContain("Missing right now", text);
    }
}
