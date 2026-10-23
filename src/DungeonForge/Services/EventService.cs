using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Models;

namespace DungeonForge.Services;

public sealed record CreateEventResult(EventInstance? Event, string? Error);

/// <summary>
/// Orchestrates events in Discord: posting the embed + thread, handling sign-ups via reactions,
/// cancelling, and reconciling with Discord after a restart. All mutations are serialized by one gate.
/// </summary>
public sealed class EventService
{
    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly IEventStore _store;
    private readonly EventRenderer _renderer;
    private readonly IBotLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EventService(DiscordSocketClient client, BotConfiguration config, ResolvedChannels channels,
        IEventStore store, EventRenderer renderer, IBotLog log)
    {
        _client = client;
        _config = config;
        _channels = channels;
        _store = store;
        _renderer = renderer;
        _log = log;
    }

    private ulong GuildId => _config.Global.GuildId;

    private SocketGuild? Guild => _client.GetGuild(GuildId);

    private SocketTextChannel? EventsChannel => Guild?.GetTextChannel(_channels.Events);

    /// <summary>Snapshot of active events ordered by start time.</summary>
    public async Task<IReadOnlyList<EventInstance>> ListEventsAsync()
    {
        await _gate.WaitAsync();
        try { return _store.Events.OrderBy(e => e.StartTime).ToList(); }
        finally { _gate.Release(); }
    }

    // ------------------------------------------------------------------ create

    public async Task<CreateEventResult> CreateEventAsync(EventDefinition def, DateTimeOffset start, ulong createdByUserId)
    {
        await _gate.WaitAsync();
        try
        {
            if (_store.Events.Any(e => e.EventName.Equals(def.Name, StringComparison.OrdinalIgnoreCase) && e.StartTime == start))
                return new CreateEventResult(null, $"An event '{def.Name}' at {TimeZoneInfo.ConvertTime(start, _config.TimeZone):dd.MM.yyyy HH:mm} already exists.");

            var channel = EventsChannel;
            if (channel is null)
                return new CreateEventResult(null, "The events channel could not be found. Check global.json and the bot's permissions.");

            var instance = new EventInstance
            {
                EventName = def.Name,
                DisplayName = def.DisplayName,
                AttendiesRequired = def.AttendiesRequired,
                // canonicalize the role keys to the spelling used in subscription-roles.json
                AttendyPerRole = def.AttendyPerRole.ToDictionary(
                    kv => _config.FindRole(kv.Key)?.Key ?? kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                StartTime = start,
                DurationInHours = def.DurationInHours,
                GuildId = GuildId,
                ChannelId = channel.Id,
                CreatedByUserId = createdByUserId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            RestUserMessage message;
            try
            {
                message = await channel.SendMessageAsync(embed: _renderer.BuildEmbed(instance), allowedMentions: AllowedMentions.None);
            }
            catch (HttpException ex)
            {
                _log.Error($"Could not post the event message in #{channel.Name}. Does the bot have View Channel, Send Messages and Embed Links there?", ex);
                return new CreateEventResult(null, "I could not post in the events channel (missing permissions?). See the log channel/console.");
            }

            instance.MessageId = message.Id;
            _store.Add(instance);
            await _store.SaveAsync(); // persist right away so a crash cannot orphan the message

            await CreateThreadAsync(channel, message, instance);
            await EnsureBotReactionsAsync(message, instance);
            await _store.SaveAsync();

            _log.Info($"Event **{instance.DisplayName}** ({instance.EventName}) created for {_renderer.FormatLocal(instance)} by {EventRenderer.Mention(createdByUserId)}: " +
                      DiscordHelpers.MessageLink(GuildId, channel.Id, message.Id));
            return new CreateEventResult(instance, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CreateThreadAsync(SocketTextChannel channel, IUserMessage message, EventInstance instance)
    {
        try
        {
            var thread = await channel.CreateThreadAsync(_renderer.ThreadName(instance), ThreadType.PublicThread,
                ThreadArchiveDuration.OneWeek, message);
            instance.ThreadId = thread.Id;

            await thread.SendMessageAsync(
                $"💬 Thread for **{instance.DisplayName}** ({_renderer.FormatLocal(instance)}). Sign up by reacting to the event message in <#{channel.Id}>.",
                allowedMentions: AllowedMentions.None);

            if (_config.Global.LockEventThreads)
                await thread.ModifyAsync(p => p.Locked = true);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not create/lock the thread for **{instance.DisplayName}** (needs Create Public Threads, Send Messages in Threads and Manage Threads): {ex.Message}");
        }
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
            if (e is null) return "That event does not exist (anymore).";

            var message = await GetMessageAsync(e.MessageId);
            if (message is not null)
            {
                try
                {
                    await message.ModifyAsync(m => m.Embed = _renderer.BuildEmbed(e, cancelled: true));
                    await message.RemoveAllReactionsAsync();
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not fully update the cancelled event message: {ex.Message}");
                }
            }

            if (e.ThreadId != 0 && _client.GetChannel(e.ThreadId) is SocketThreadChannel thread)
            {
                try
                {
                    await thread.SendMessageAsync("❌ This event was cancelled.");
                    await thread.ModifyAsync(p => { p.Locked = true; p.Archived = true; });
                }
                catch (Exception ex)
                {
                    _log.Warn($"Could not archive the thread of the cancelled event: {ex.Message}");
                }
            }

            _store.Remove(e);
            await _store.SaveAsync();
            _log.Info($"Event **{e.DisplayName}** ({_renderer.FormatLocal(e)}) was cancelled by {EventRenderer.Mention(cancelledByUserId)}.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ reactions

    public async Task HandleReactionAsync(ulong channelId, ulong messageId, ulong userId, IEmote emote, bool added)
    {
        if (channelId != _channels.Events || userId == _client.CurrentUser?.Id) return;

        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null) return;

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
            await RemoveReactionAsync(e.MessageId, emote, userId); // not one of the sign-up emoji
            return;
        }

        if (!await IsDungeoneerAsync(userId))
        {
            await RemoveReactionAsync(e.MessageId, emote, userId);
            _log.Warn($"{EventRenderer.Mention(userId)} tried to sign up for **{e.DisplayName}** but does not have the role '{_config.Global.Roles.Dungeoneer.Name}'. Reaction removed.");
            return;
        }

        var result = SignupLogic.Apply(e, userId, role.Key, DateTimeOffset.UtcNow);
        if (!result.Changed) return;

        if (result.PreviousRoleKey is not null)
        {
            var previous = _config.FindRole(result.PreviousRoleKey);
            if (previous is not null)
                await RemoveReactionAsync(e.MessageId, EmojiKey.ToEmote(previous.Emoji), userId);
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
        // Only a removal of the reaction matching the current role is a sign-off. When the bot itself removes the
        // previous reaction after a role change, the stored role is already the new one and nothing happens here.
        if (!SignupLogic.Remove(e, userId, role.Key)) return;

        await _store.SaveAsync();
        await UpdateMessageAsync(e);
        _log.Info($"➖ {EventRenderer.Mention(userId)} signed off from **{e.DisplayName}** ({role.DisplayName}) " +
                  $"({SignupLogic.CountAttending(e)}/{e.AttendiesRequired}).");
    }

    /// <summary>Called when somebody deletes an event message in the events channel.</summary>
    public async Task HandleMessageDeletedAsync(ulong channelId, ulong messageId)
    {
        if (channelId != _channels.Events) return;
        await _gate.WaitAsync();
        try
        {
            var e = _store.FindByMessageId(messageId);
            if (e is null) return;
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
            foreach (var e in finished) _store.Remove(e);
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
        var message = await GetMessageAsync(e.MessageId);
        if (message is null)
        {
            _store.Remove(e);
            _log.Warn($"The message of event **{e.DisplayName}** ({_renderer.FormatLocal(e)}) no longer exists; the event was removed.");
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
            if (role is not null) await RemoveReactionAsync(e.MessageId, EmojiKey.ToEmote(role.Emoji), removal.UserId);
        }

        // remove foreign emoji, add missing bot reactions
        var offeredKeys = roles.Select(r => EmojiKey.FromConfigured(r.Emoji)).ToHashSet();
        foreach (var (emote, meta) in message.Reactions.ToList())
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

    private async Task<bool> IsDungeoneerAsync(ulong userId)
    {
        var guild = Guild;
        if (guild is null) return false;
        var user = await DiscordHelpers.GetGuildUserAsync(_client, GuildId, userId);
        return user is not null && DiscordHelpers.IsInRole(user, guild, _config.Global.Roles.Dungeoneer);
    }

    private async Task<IUserMessage?> GetMessageAsync(ulong messageId)
    {
        var channel = EventsChannel;
        if (channel is null) return null;
        try
        {
            return await channel.GetMessageAsync(messageId) as IUserMessage;
        }
        catch (Exception ex) when (DiscordHelpers.IsNotFound(ex))
        {
            return null;
        }
    }

    private async Task RemoveReactionAsync(ulong messageId, IEmote emote, ulong userId)
    {
        try
        {
            var message = await GetMessageAsync(messageId);
            if (message is not null)
                await message.RemoveReactionAsync(emote, userId);
        }
        catch (Exception ex)
        {
            _log.Warn($"Could not remove a reaction (the bot needs Manage Messages in the events channel): {ex.Message}");
        }
    }

    private async Task UpdateMessageAsync(EventInstance e)
    {
        var channel = EventsChannel;
        if (channel is null) return;
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
