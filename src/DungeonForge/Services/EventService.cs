using Discord;
using Discord.Net;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

public sealed record CreateEventResult(EventInstance? Event, string? Error);

public readonly record struct CommandOutcome(bool Ok, string Message);

public sealed record CompleteEventOutcome(bool Ok, bool AlreadyCompleted, IReadOnlyList<string> Messages);

/// <summary>
/// Orchestrates events in Discord: a standalone public thread per event, the embed and reactions inside that thread,
/// sign-ups, boss kills, completion and payout. All mutations are serialized by one gate.
/// </summary>
public sealed class EventService
{
    private static readonly ThreadArchiveDuration[] ArchiveDurations =
    [
        ThreadArchiveDuration.OneWeek,
        ThreadArchiveDuration.ThreeDays,
        ThreadArchiveDuration.OneDay,
    ];

    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly IEventStore _store;
    private readonly IPointsStore _points;
    private readonly EventDefinitionRepository _definitions;
    private readonly EventRenderer _renderer;
    private readonly IBotLog _log;
    private readonly PointsBoardService _board;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EventService(
        DiscordSocketClient client,
        BotConfiguration config,
        ResolvedChannels channels,
        IEventStore store,
        IPointsStore points,
        EventDefinitionRepository definitions,
        EventRenderer renderer,
        IBotLog log,
        PointsBoardService board)
    {
        _client = client;
        _config = config;
        _channels = channels;
        _store = store;
        _points = points;
        _definitions = definitions;
        _renderer = renderer;
        _log = log;
        _board = board;
    }

    private ulong GuildId => _config.Global.GuildId;

    private SocketGuild? Guild => _client.GetGuild(GuildId);

    private SocketTextChannel? EventsChannel => Guild?.GetTextChannel(_channels.Events);

    /// <summary>Snapshot of stored events ordered by start time. Completed events stay until the retention cutoff.</summary>
    public async Task<IReadOnlyList<EventInstance>> ListEventsAsync()
    {
        await _gate.WaitAsync();
        try { return _store.Events.OrderBy(e => e.StartTime).ToList(); }
        finally { _gate.Release(); }
    }

    public async Task<bool> IsEventThreadAsync(ulong threadId)
    {
        if (threadId == 0)
            return false;

        await _gate.WaitAsync();
        try { return _store.FindByThreadId(threadId) is not null; }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> PointsForAsync(ulong userId)
    {
        await _gate.WaitAsync();
        try
        {
            var total = PointsLogic.Total(_points.Entries, userId);
            var recent = PointsLogic.Recent(_points.Entries, userId, PointsLogic.RecentEntryCount);
            return PointsSummaryFormatter.FormatUser(userId, total, recent);
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<string>> LeaderboardAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return PointsSummaryFormatter.FormatLeaderboard(PointsLogic.Leaderboard(_points.Entries), PointsLogic.LeaderboardSize);
        }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ create

    public async Task<CreateEventResult> CreateEventAsync(EventDefinition def, DateTimeOffset start, ulong createdByUserId, bool awardsPoints)
    {
        await _gate.WaitAsync();
        try
        {
            if (_store.Events.Any(e => e.EventName.Equals(def.Name, StringComparison.OrdinalIgnoreCase) && e.StartTime == start))
                return new CreateEventResult(null, $"An event '{def.Name}' at {TimeZoneInfo.ConvertTime(start, _config.TimeZone):dd.MM.yyyy HH:mm} already exists.");

            var open = _store.Events.Count(e => e.Status == EventStatus.Open);
            if (OpenEventLimit.Rejection(open, _config.Global.MaxOpenEvents) is { } limitError)
                return new CreateEventResult(null, limitError);

            var channel = EventsChannel;
            if (channel is null)
                return new CreateEventResult(null, "The events channel could not be found. Check global.json and the bot's permissions.");

            var instance = new EventInstance
            {
                EventName = def.Name,
                DisplayName = def.DisplayName,
                AttendiesRequired = def.AttendiesRequired,
                AttendyPerRole = def.AttendyPerRole.ToDictionary(
                    kv => _config.FindRole(kv.Key)?.Key ?? kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                StartTime = start,
                DurationInHours = def.DurationInHours,
                GuildId = GuildId,
                ChannelId = channel.Id,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                AwardsPoints = awardsPoints,
                Status = EventStatus.Open,
                EmbedInThread = true,
            };

            // Standalone public thread: no parent message. Discord.Net 3.20 names this CreateThreadAsync;
            // omitting the message posts to POST /channels/{id}/threads.
            var thread = await CreateStandaloneThreadAsync(channel, _renderer.ThreadName(instance));
            if (thread is null)
                return new CreateEventResult(null, "I could not create a thread in the events channel (needs Create Public Threads). See the log channel/console.");

            await EnsureActiveAsync(thread);

            IUserMessage posted;
            try
            {
                posted = await thread.SendMessageAsync(embed: _renderer.BuildEmbed(instance), allowedMentions: AllowedMentions.None);
            }
            catch (HttpException ex)
            {
                _log.Error($"Could not post the event embed in the thread for **{instance.DisplayName}** (needs Send Messages in Threads and Embed Links).", ex);
                await TryDeleteThreadAsync(thread);
                return new CreateEventResult(null, "I could not post the event inside its thread (missing permissions?). See the log channel/console.");
            }

            instance.ThreadId = thread.Id;
            instance.MessageId = posted.Id;
            _store.Add(instance);
            await _store.SaveAsync();

            await EnsureBotReactionsAsync(posted, instance);
            if (_config.Global.LockEventThreads)
            {
                try { await thread.ModifyAsync(p => p.Locked = true); }
                catch (Exception ex)
                {
                    _log.Warn($"Could not lock the thread for **{instance.DisplayName}** (needs Manage Threads): {ex.Message}");
                }
            }

            await _store.SaveAsync();
            var pointsNote = instance.AwardsPoints ? " Points are enabled." : " Points are disabled.";
            _log.Info($"Event **{instance.DisplayName}** ({instance.EventName}) created for {_renderer.FormatLocal(instance)} by {EventRenderer.Mention(createdByUserId)}.{pointsNote} " +
                      DiscordHelpers.MessageLink(GuildId, EventMessagePolicy.LinkChannelId(instance), posted.Id));
            return new CreateEventResult(instance, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IThreadChannel?> CreateStandaloneThreadAsync(SocketTextChannel channel, string name)
    {
        HttpException? last = null;
        foreach (var duration in ArchiveDurations)
        {
            try
            {
                return await channel.CreateThreadAsync(name, ThreadType.PublicThread, duration);
            }
            catch (HttpException ex) when ((int)ex.HttpCode == 400)
            {
                last = ex;
            }
            catch (HttpException ex)
            {
                _log.Warn($"Could not create the event thread (needs Create Public Threads): {ex.Message}");
                return null;
            }
        }

        _log.Warn($"Could not create the event thread: {last?.Message}");
        return null;
    }

    private async Task EnsureBotReactionsAsync(IUserMessage message, EventInstance instance)
    {
        try
        {
            var emotes = _renderer.RolesForEvent(instance).Select(r => EmojiKey.ToEmote(r.Emoji)).ToArray();
            await message.AddReactionsAsync(emotes);
        }
        catch (Exception ex)
        {
            _log.Error($"Could not add the role reactions to the event message of **{instance.DisplayName}** (check the emoji in the role files and the Add Reactions permission).", ex);
        }
    }

    // ------------------------------------------------------------------ cancel

    /// <summary>Cancels the event identified by its message id. Returns an error text, or null on success.</summary>
    public async Task<string?> CancelEventAsync(ulong messageId, ulong cancelledByUserId)
    {
        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null)
                return "That event does not exist (anymore).";

            var keptPoints = e.Status == EventStatus.Completed && e.AwardsPoints;
            var channel = await OpenEventChannelAsync(e);
            if (channel is not null)
            {
                try
                {
                    await channel.ModifyMessageAsync(e.MessageId, m => m.Embed = _renderer.BuildEmbed(e, cancelled: true));
                    if (await channel.GetMessageAsync(e.MessageId) is IUserMessage message)
                        await message.RemoveAllReactionsAsync();
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not fully update the cancelled event message: {ex.Message}");
                }
            }

            if (e.ThreadId != 0)
            {
                var thread = await FindThreadAsync(e.ThreadId);
                if (thread is not null)
                {
                    try
                    {
                        await EnsureActiveAsync(thread);
                        await thread.SendMessageAsync("❌ This event was cancelled.");
                        await thread.ModifyAsync(p => { p.Locked = true; p.Archived = true; });
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"Could not archive the thread of the cancelled event: {ex.Message}");
                    }
                }
            }

            _store.Remove(e);
            await _store.SaveAsync();
            var kept = keptPoints ? " Points already paid are kept." : "";
            _log.Info($"Event **{e.DisplayName}** ({_renderer.FormatLocal(e)}) was cancelled by {EventRenderer.Mention(cancelledByUserId)}.{kept}");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ points

    public Task<CommandOutcome> RecordBossKillAsync(ulong messageId, string boss, ulong actorId) =>
        MutateAsync(messageId, actorId, e =>
        {
            if (!TryBosses(e, out var bosses, out var error))
                return new PointsLogic.RosterResult(false, error ?? "Could not load the raid template.");
            return PointsLogic.RecordKill(e, boss, bosses, DateTimeOffset.UtcNow);
        }, "💀");

    public Task<CommandOutcome> UndoBossKillAsync(ulong messageId, string boss, ulong actorId) =>
        MutateAsync(messageId, actorId, e => PointsLogic.UndoKill(e, boss), "↩️");

    public Task<CommandOutcome> ExcludePlayerAsync(ulong messageId, ulong userId, ulong actorId) =>
        MutateAsync(messageId, actorId, e => PointsLogic.Exclude(e, userId), "🚫", userId);

    public Task<CommandOutcome> IncludePlayerAsync(ulong messageId, ulong userId, ulong actorId) =>
        MutateAsync(messageId, actorId, e => PointsLogic.Include(e, userId), "✅", userId);

    public Task<CommandOutcome> EditKillAsync(ulong messageId, string boss, ulong userId, bool add, ulong actorId) =>
        MutateAsync(messageId, actorId, e => PointsLogic.EditAttendee(e, boss, userId, add), add ? "➕" : "➖", userId);

    public async Task<CompleteEventOutcome> CompleteEventAsync(ulong messageId, ulong actorId)
    {
        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null)
                return new CompleteEventOutcome(false, false, new[] { "That event does not exist (anymore)." });

            var pointsFor = PointsFor(e);
            var result = PointsLogic.Complete(
                e,
                pointsFor,
                _config.Global.BenchPointsPercent,
                DateTimeOffset.UtcNow,
                _points.HasEvent(e.MessageId));

            if (!result.AlreadyCompleted && result.Entries.Count > 0)
            {
                _points.AddRange(result.Entries);
                await _points.SaveAsync();
            }

            await _store.SaveAsync();
            await UpdateMessageAsync(e);
            if (_config.Global.ArchiveCompletedEventThreads)
                await ArchiveThreadAsync(e);

            var messages = PointsSummaryFormatter.FormatCompletion(e, result.AlreadyCompleted, result.Entries, pointsFor, maxLength: 1800);
            foreach (var message in messages)
                _log.Info($"{EventRenderer.Mention(actorId)} completed **{e.DisplayName}**.\n{message}");

            if (!result.AlreadyCompleted)
                await _board.RefreshLeaderboardAsync();

            return new CompleteEventOutcome(true, result.AlreadyCompleted, messages);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ reactions

    public async Task HandleReactionAsync(ulong channelId, ulong messageId, ulong userId, IEmote emote, bool added)
    {
        if (userId == _client.CurrentUser?.Id)
            return;

        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null)
                return;
            if (!EventMessagePolicy.ReactionBelongsToEvent(e, channelId, _channels.Events))
                return;

            if (e.Status == EventStatus.Completed)
            {
                if (added)
                    await RemoveReactionAsync(e, emote, userId);
                if (_config.Global.ArchiveCompletedEventThreads)
                    await ArchiveThreadAsync(e);
                return;
            }

            var role = _config.FindRoleByEmojiKey(EmojiKey.FromReaction(emote));
            var offered = role is not null && _renderer.RolesForEvent(e).Any(r => r.Key == role.Key);

            if (added)
                await OnReactionAddedAsync(e, userId, emote, role, offered);
            else if (role is not null)
                await OnReactionRemovedAsync(e, userId, role);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OnReactionAddedAsync(EventInstance e, ulong userId, IEmote emote, RoleDefinition? role, bool offered)
    {
        if (role is null || !offered)
        {
            await RemoveReactionAsync(e, emote, userId);
            return;
        }

        if (!await IsDungeoneerAsync(userId))
        {
            await RemoveReactionAsync(e, emote, userId);
            _log.Warn($"{EventRenderer.Mention(userId)} tried to sign up for **{e.DisplayName}** but does not have the role '{_config.Global.Roles.Dungeoneer.Name}'. Reaction removed.");
            return;
        }

        var result = SignupLogic.Apply(e, userId, role.Key, DateTimeOffset.UtcNow);
        if (!result.Changed)
            return;

        if (result.PreviousRoleKey is not null)
        {
            var previous = _config.FindRole(result.PreviousRoleKey);
            if (previous is not null)
                await RemoveReactionAsync(e, EmojiKey.ToEmote(previous.Emoji), userId);
        }

        await _store.SaveAsync();
        await UpdateMessageAsync(e);

        var count = $"{SignupLogic.CountAttending(e)}/{e.AttendiesRequired}";
        _log.Info(result.PreviousRoleKey is null
            ? $"➕ {EventRenderer.Mention(userId)} signed up for **{e.DisplayName}** as **{role.DisplayName}** ({count})."
            : $"🔁 {EventRenderer.Mention(userId)} changed from **{result.PreviousRoleKey}** to **{role.DisplayName}** for **{e.DisplayName}** ({count}).");
    }

    private async Task OnReactionRemovedAsync(EventInstance e, ulong userId, RoleDefinition role)
    {
        if (!SignupLogic.Remove(e, userId, role.Key))
            return;

        await _store.SaveAsync();
        await UpdateMessageAsync(e);
        _log.Info($"➖ {EventRenderer.Mention(userId)} signed off from **{e.DisplayName}** ({role.DisplayName}) " +
                  $"({SignupLogic.CountAttending(e)}/{e.AttendiesRequired}).");
    }

    /// <summary>Called when somebody deletes an event message (in the events channel or inside its thread).</summary>
    public async Task HandleMessageDeletedAsync(ulong channelId, ulong messageId)
    {
        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null)
                return;
            if (channelId != _channels.Events && channelId != e.ChannelId && channelId != e.ThreadId)
                return;

            _store.Remove(e);
            await _store.SaveAsync();
            _log.Warn($"The message of event **{e.DisplayName}** ({_renderer.FormatLocal(e)}) was deleted, so the event was removed.");
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ startup reconciliation

    /// <summary>Prunes finished events and reconciles every stored event with the reactions on its message.</summary>
    public async Task ReconcileAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-_config.Global.FinishedEventRetentionHours);
            var finished = _store.Events.Where(e => e.EndTime < cutoff).ToList();
            foreach (var e in finished)
                _store.Remove(e);
            if (finished.Count > 0)
                _log.Info($"Forgot {finished.Count} finished event(s).");

            foreach (var e in _store.Events.ToList())
            {
                try { await ReconcileOneAsync(e); }
                catch (Exception ex) { _log.Error($"Could not reconcile event **{e.DisplayName}** ({e.EventName}).", ex); }
            }

            await _store.SaveAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReconcileOneAsync(EventInstance e)
    {
        var channel = await OpenEventChannelAsync(e);
        if (channel is null)
        {
            _log.Warn($"Could not reach event **{e.DisplayName}** ({_renderer.FormatLocal(e)}). It was left in the state file.");
            return;
        }

        IUserMessage? message;
        try
        {
            message = await channel.GetMessageAsync(e.MessageId) as IUserMessage;
        }
        catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
        {
            message = null;
        }

        if (message is null)
        {
            _store.Remove(e);
            _log.Warn($"The message of event **{e.DisplayName}** ({_renderer.FormatLocal(e)}) no longer exists; the event was removed.");
            return;
        }

        if (e.Status == EventStatus.Completed)
        {
            await message.ModifyAsync(m => m.Embed = _renderer.BuildEmbed(e));
            if (_config.Global.ArchiveCompletedEventThreads)
                await ArchiveThreadAsync(e);
            return;
        }

        var selfId = _client.CurrentUser.Id;
        var roles = _renderer.RolesForEvent(e);

        var reactionsByRole = new Dictionary<string, IReadOnlyCollection<ulong>>();
        foreach (var role in roles)
        {
            var users = await message.GetReactionUsersAsync(EmojiKey.ToEmote(role.Emoji), 500).FlattenAsync();
            reactionsByRole[role.Key] = users.Where(u => !u.IsBot && u.Id != selfId).Select(u => u.Id).ToList();
        }

        var eligibility = new Dictionary<ulong, bool>();
        foreach (var userId in reactionsByRole.Values.SelectMany(v => v).Distinct())
            eligibility[userId] = await IsDungeoneerAsync(userId);

        var before = e.Signups.Select(s => (s.UserId, s.RoleKey)).OrderBy(x => x.UserId).ToList();
        var plan = SignupLogic.Reconcile(e, reactionsByRole, roles.Select(r => r.Key).ToList(),
            u => eligibility.GetValueOrDefault(u), DateTimeOffset.UtcNow);
        e.Signups = plan.Signups;
        var after = e.Signups.Select(s => (s.UserId, s.RoleKey)).OrderBy(x => x.UserId).ToList();

        foreach (var removal in plan.ReactionsToRemove)
        {
            var role = _config.FindRole(removal.RoleKey);
            if (role is not null)
                await RemoveReactionAsync(e, EmojiKey.ToEmote(role.Emoji), removal.UserId);
        }

        var offeredKeys = roles.Select(r => EmojiKey.FromConfigured(r.Emoji)).ToHashSet();
        foreach (var (emote, _) in message.Reactions.ToList())
        {
            if (!offeredKeys.Contains(EmojiKey.FromReaction(emote)))
            {
                try { await message.RemoveAllReactionsForEmoteAsync(emote); }
                catch (Exception ex) { _log.Warn($"Could not remove stray reaction from the event message: {ex.Message}"); }
            }
        }

        var present = message.Reactions.Where(kv => kv.Value.IsMe).Select(kv => EmojiKey.FromReaction(kv.Key)).ToHashSet();
        var missing = roles.Where(r => !present.Contains(EmojiKey.FromConfigured(r.Emoji)))
            .Select(r => EmojiKey.ToEmote(r.Emoji)).ToArray();
        if (missing.Length > 0)
            await message.AddReactionsAsync(missing);

        await UpdateMessageAsync(e);

        if (!before.SequenceEqual(after))
            _log.Info($"Event **{e.DisplayName}** was reconciled after restart: sign-ups changed while I was offline ({SignupLogic.CountAttending(e)}/{e.AttendiesRequired}).");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<CommandOutcome> MutateAsync(
        ulong messageId,
        ulong actorId,
        Func<EventInstance, PointsLogic.RosterResult> mutate,
        string verb,
        ulong? subjectUserId = null)
    {
        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null)
                return new CommandOutcome(false, "That event does not exist (anymore).");

            var result = mutate(e);
            if (!result.Ok)
                return new CommandOutcome(false, result.Message);

            await _store.SaveAsync();
            await UpdateMessageAsync(e);
            var subject = subjectUserId is ulong id ? $" {EventRenderer.Mention(id)}" : "";
            _log.Info($"{verb} {EventRenderer.Mention(actorId)}{subject} on **{e.DisplayName}**: {result.Message}");
            return new CommandOutcome(true, result.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryBosses(EventInstance e, out IReadOnlyList<BossDefinition> bosses, out string? error)
    {
        try
        {
            var def = _definitions.Load(e.EventName);
            bosses = def.Bosses ?? new List<BossDefinition>();
            error = null;
            return true;
        }
        catch (ConfigurationException ex)
        {
            bosses = Array.Empty<BossDefinition>();
            error = string.Join(" ", ex.Errors);
            return false;
        }
    }

    private Func<string, int> PointsFor(EventInstance e)
    {
        EventDefinition? def = null;
        try
        {
            def = _definitions.Load(e.EventName);
        }
        catch (ConfigurationException ex)
        {
            _log.Warn($"Could not load the template '{e.EventName}' while paying points ({string.Join(" ", ex.Errors)}). Using pointsPerBossKill ({_config.Global.PointsPerBossKill}) for each boss.");
        }

        var perKill = _config.Global.PointsPerBossKill;
        return boss => PointsLogic.ResolvePoints(
            def?.Bosses?.FirstOrDefault(b => b.Name.Equals(boss, StringComparison.OrdinalIgnoreCase)),
            perKill);
    }

    private async Task<bool> IsDungeoneerAsync(ulong userId)
    {
        var guild = Guild;
        if (guild is null)
            return false;
        var user = await DiscordHelpers.GetGuildUserAsync(_client, GuildId, userId);
        return user is not null && DiscordHelpers.IsInRole(user, guild, _config.Global.Roles.Dungeoneer);
    }

    private async Task<IMessageChannel?> OpenEventChannelAsync(EventInstance e)
    {
        if (!e.EmbedInThread)
            return EventsChannel;

        var thread = await FindThreadAsync(e.ThreadId);
        if (thread is null)
            return null;

        await EnsureActiveAsync(thread);
        return thread;
    }

    private async Task<IThreadChannel?> FindThreadAsync(ulong threadId)
    {
        if (threadId == 0)
            return null;
        if (_client.GetChannel(threadId) is IThreadChannel cached)
            return cached;

        try
        {
            return await _client.Rest.GetChannelAsync(threadId) as IThreadChannel;
        }
        catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not load thread {threadId}: {ex.Message}");
            return null;
        }
    }

    private async Task EnsureActiveAsync(IThreadChannel thread)
    {
        try
        {
            if (thread.IsArchived)
                await thread.ModifyAsync(p => p.Archived = false);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not unarchive the thread **{thread.Name}**: {ex.Message}");
        }

        try
        {
            await thread.JoinAsync();
        }
        catch
        {
            // Already a member, or the thread cannot be joined. Editing may still work.
        }
    }

    private async Task ArchiveThreadAsync(EventInstance e)
    {
        if (e.ThreadId == 0)
            return;

        var thread = await FindThreadAsync(e.ThreadId);
        if (thread is null)
            return;

        try
        {
            await thread.ModifyAsync(p => { p.Locked = true; p.Archived = true; });
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not archive the thread of **{e.DisplayName}**: {ex.Message}");
        }
    }

    private async Task TryDeleteThreadAsync(IThreadChannel thread)
    {
        try { await thread.DeleteAsync(); }
        catch (Exception ex) { _log.Warn($"Could not delete an empty event thread: {ex.Message}"); }
    }

    private async Task RemoveReactionAsync(EventInstance e, IEmote emote, ulong userId)
    {
        try
        {
            var channel = await OpenEventChannelAsync(e);
            if (channel is null)
                return;
            if (await channel.GetMessageAsync(e.MessageId) is IUserMessage message)
                await message.RemoveReactionAsync(emote, userId);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not remove a reaction (the bot needs Manage Messages): {ex.Message}");
        }
    }

    private async Task UpdateMessageAsync(EventInstance e)
    {
        var channel = await OpenEventChannelAsync(e);
        if (channel is null)
        {
            _log.Warn($"Could not reach the message of **{e.DisplayName}** to update it.");
            return;
        }

        try
        {
            await channel.ModifyMessageAsync(e.MessageId, m => m.Embed = _renderer.BuildEmbed(e));
        }
        catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
        {
            _store.Remove(e);
            await _store.SaveAsync();
            _log.Warn($"The message of event **{e.DisplayName}** disappeared; the event was removed.");
        }
        catch (Exception ex)
        {
            _log.Error($"Could not update the message of event **{e.DisplayName}**.", ex);
        }
    }
}
