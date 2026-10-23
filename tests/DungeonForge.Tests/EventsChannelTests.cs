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
        Assert.Empty(EventsChannelSupport.MissingPermissions(granted, EventsChannelKind.Text));

        var missingSend = granted with { SendMessages = false, EmbedLinks = false };
        var missing = EventsChannelSupport.MissingPermissions(missingSend, EventsChannelKind.Text);
        Assert.Equal(new[] { "Send Messages", "Embed Links" }, missing);

        var textMissing = EventsChannelSupport.StartupMessage("events", EventsChannelKind.Text, missing);
        Assert.Contains("text channel", textMissing);
        Assert.Contains("Missing right now: Send Messages, Embed Links.", textMissing);

        var text = EventsChannelSupport.StartupMessage("events", EventsChannelKind.Text, Array.Empty<string>());
        Assert.Contains("text channel", text);
        Assert.Contains("Create Public Threads", text);
        Assert.DoesNotContain("Missing right now", text);
    }

    [Fact]
    public void MissingPermissions_ForumDoesNotReportCreatePublicThreads()
    {
        var forumChannel = new EventsChannelPermissionSnapshot(
            ViewChannel: true,
            SendMessages: true,
            CreatePublicThreads: false,
            SendMessagesInThreads: true,
            ManageThreads: true,
            ManageMessages: true,
            AddReactions: true,
            ReadMessageHistory: true,
            EmbedLinks: true);

        var missing = EventsChannelSupport.MissingPermissions(forumChannel, EventsChannelKind.Forum);
        Assert.Empty(missing);

        var report = EventsChannelSupport.StartupMessage("dungeon-forge-forum", EventsChannelKind.Forum, missing);
        Assert.Contains("forum", report);
        Assert.Contains("Create Posts", report);
        Assert.Contains("Send Messages in Posts", report);
        Assert.DoesNotContain("Create Public Threads", report);
        Assert.DoesNotContain("Missing right now", report);

        var missingPosts = EventsChannelSupport.MissingPermissions(
            forumChannel with { SendMessages = false, EmbedLinks = false },
            EventsChannelKind.Forum);
        Assert.Equal(new[] { "Create Posts", "Embed Links" }, missingPosts);
        var warned = EventsChannelSupport.StartupMessage("dungeon-forge-forum", EventsChannelKind.Forum, missingPosts);
        Assert.Contains("Missing right now: Create Posts, Embed Links.", warned);
        Assert.DoesNotContain("Create Public Threads", warned);
    }

    [Fact]
    public void MissingPermissions_TextChannelStillReportsCreatePublicThreads()
    {
        var textChannel = new EventsChannelPermissionSnapshot(true, true, false, true, true, true, true, true, true);
        var missing = EventsChannelSupport.MissingPermissions(textChannel, EventsChannelKind.Text);
        Assert.Equal(new[] { "Create Public Threads" }, missing);

        var report = EventsChannelSupport.StartupMessage("events", EventsChannelKind.Text, missing);
        Assert.Contains("Missing right now: Create Public Threads.", report);
    }
}
