using Discord;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;

namespace DungeonForge.Services;

/// <summary>
/// Keeps the points-channel leaderboard and raid boss table up to date by editing the same messages.
/// A missing channel is logged once and skipped.
/// </summary>
public sealed class PointsBoardService
{
    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly IPointsStore _points;
    private readonly IPointsChannelStore _state;
    private readonly EventDefinitionRepository _definitions;
    private readonly IBotLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _missingWarned;

    public PointsBoardService(
        DiscordSocketClient client,
        BotConfiguration config,
        ResolvedChannels channels,
        IPointsStore points,
        IPointsChannelStore state,
        EventDefinitionRepository definitions,
        IBotLog log)
    {
        _client = client;
        _config = config;
        _channels = channels;
        _points = points;
        _state = state;
        _definitions = definitions;
        _log = log;
    }

    public async Task RefreshLeaderboardAsync() =>
        await RefreshAsync(async channel =>
        {
            var pages = PointsBoardFormatter.FormatLeaderboard(PointsLogic.Leaderboard(_points.Entries));
            var previous = _state.LeaderboardMessageIds.ToList();
            // Extra leaderboard messages are created at the bottom of the channel. Delete the boss table first
            // when the page count grows, then post it again after the leaderboard so the order stays leaderboard, then bosses.
            var repostBossTable = PointsBoardFormatter.LeaderboardGrowthMovesBossTable(
                previous.Count, pages.Count, _state.BossTableMessageIds.Count);
            if (repostBossTable)
                await DeleteBossTableAsync(channel);

            var next = new List<ulong>();
            for (var i = 0; i < pages.Count; i++)
            {
                var existing = i < previous.Count ? previous[i] : 0UL;
                next.Add(await UpsertAsync(channel, existing, pages[i]));
            }

            for (var i = pages.Count; i < previous.Count; i++)
                await TryDeleteAsync(channel, previous[i]);

            _state.LeaderboardMessageIds.Clear();
            _state.LeaderboardMessageIds.AddRange(next);

            if (repostBossTable)
                await PostBossTableAsync(channel);
        });

    public async Task RefreshBossTableAsync() =>
        await RefreshAsync(PostBossTableAsync);

    private async Task PostBossTableAsync(SocketTextChannel channel)
    {
        var (templates, _) = _definitions.LoadAll();
        var messages = PointsBoardFormatter.FormatBossTable(templates, _config.Global.PointsPerBossKill);
        if (messages.Count == 0)
            messages = new[] { "**Raid boss points**\nNo raid templates found." };

        var previous = _state.BossTableMessageIds.ToList();
        var next = new List<ulong>();
        for (var i = 0; i < messages.Count; i++)
        {
            var existing = i < previous.Count ? previous[i] : 0UL;
            next.Add(await UpsertAsync(channel, existing, messages[i]));
        }

        for (var i = messages.Count; i < previous.Count; i++)
            await TryDeleteAsync(channel, previous[i]);

        _state.BossTableMessageIds.Clear();
        _state.BossTableMessageIds.AddRange(next);
    }

    private async Task DeleteBossTableAsync(SocketTextChannel channel)
    {
        foreach (var id in _state.BossTableMessageIds.ToList())
            await TryDeleteAsync(channel, id);
        _state.BossTableMessageIds.Clear();
    }

    private async Task RefreshAsync(Func<SocketTextChannel, Task> write)
    {
        var channel = ChannelOrNull();
        if (channel is null)
            return;

        await _gate.WaitAsync();
        try
        {
            await write(channel);
            await _state.SaveAsync();
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not update the points channel: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private SocketTextChannel? ChannelOrNull()
    {
        if (_channels.Points == 0)
        {
            WarnOnce("The points channel is not available, so the leaderboard and boss table were not posted.");
            return null;
        }

        var channel = _client.GetGuild(_config.Global.GuildId)?.GetTextChannel(_channels.Points);
        if (channel is not null)
            return channel;

        WarnOnce("The points channel could not be found, so the leaderboard and boss table were not posted.");
        return null;
    }

    private void WarnOnce(string message)
    {
        if (Interlocked.Exchange(ref _missingWarned, 1) == 0)
            _log.Warn(message);
    }

    private async Task<ulong> UpsertAsync(SocketTextChannel channel, ulong messageId, string text)
    {
        if (text.Length > EventTemplateListFormatter.DiscordMessageLimit)
            text = text[..EventTemplateListFormatter.DiscordMessageLimit];

        if (messageId != 0)
        {
            try
            {
                if (await channel.GetMessageAsync(messageId) is IUserMessage existing)
                {
                    await existing.ModifyAsync(m =>
                    {
                        m.Content = text;
                        m.AllowedMentions = AllowedMentions.None;
                    });
                    return existing.Id;
                }
            }
            catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
            {
                // The saved message was deleted; post a new one.
            }
            catch (Exception ex)
            {
                _log.Warn($"Could not edit a points-channel message: {ex.Message}");
                return messageId;
            }
        }

        var created = await channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
        return created.Id;
    }

    private async Task TryDeleteAsync(SocketTextChannel channel, ulong messageId)
    {
        if (messageId == 0)
            return;

        try
        {
            var message = await channel.GetMessageAsync(messageId);
            if (message is not null)
                await message.DeleteAsync();
        }
        catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
        {
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not remove an old points-channel message: {ex.Message}");
        }
    }
}
