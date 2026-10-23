using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DungeonForge.Configuration;
using DungeonForge.Models;
using DungeonForge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Bot;

/// <summary>Slash commands. Manager commands need df-manager; points lookup also allows df-dungeoneer. Manage channel only.</summary>
[RequireContext(ContextType.Guild)]
public sealed class EventCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly EventDefinitionRepository _definitions;
    private readonly EventService _events;
    private readonly EventRenderer _renderer;
    private readonly IBotLog _log;
    private readonly InteractionService _interactions;

    public EventCommands(BotConfiguration config, ResolvedChannels channels, EventDefinitionRepository definitions,
        EventService events, EventRenderer renderer, IBotLog log, InteractionService interactions)
    {
        _config = config;
        _channels = channels;
        _definitions = definitions;
        _events = events;
        _renderer = renderer;
        _log = log;
        _interactions = interactions;
    }

    [ManagerOnly]
    [SlashCommand("dfcreateevent", "Create a dungeon/raid event, e.g. /dfcreateevent MoltenCore40 24.10.2026 18:00")]
    public async Task CreateEventAsync(
        [Summary("event", "Template name, for example MoltenCore40"), Autocomplete(typeof(EventFileAutocomplete))] string eventName,
        [Summary("date", "Date as dd.MM.yyyy, e.g. 24.10.2026")] string date,
        [Summary("time", "Time as HH:mm (24h), e.g. 18:00")] string time,
        [Summary("awardpoints", "Award points for boss kills. Default on for raids; dungeons cannot.")] bool? awardPoints = null)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;

        var load = LoadDefinition(eventName);
        if (load.Error is not null)
        {
            await FollowupAsync($"❌ {load.Error}", ephemeral: true);
            return;
        }

        if (PointsLogic.DungeonPointsError(load.Definition!.Type, awardPoints) is { } pointsError)
        {
            await FollowupAsync($"❌ {pointsError}", ephemeral: true);
            return;
        }

        if (!EventDateParser.TryParse(date, time, _config.TimeZone, DateTimeOffset.UtcNow, out var start, out var error))
        {
            await FollowupAsync($"❌ {error}", ephemeral: true);
            return;
        }

        var awards = PointsLogic.AwardsPointsForNewEvent(load.Definition.Type, awardPoints);
        var result = await _events.CreateEventAsync(load.Definition, start, Context.User.Id, awards);
        if (result.Event is null)
        {
            await FollowupAsync($"❌ {result.Error}", ephemeral: true);
            return;
        }

        var link = DiscordHelpers.MessageLink(Context.Guild.Id, EventMessagePolicy.LinkChannelId(result.Event), result.Event.MessageId);
        var points = result.Event.AwardsPoints ? "Points: enabled." : "Points: disabled.";
        await FollowupAsync($"✅ Event **{result.Event.DisplayName}** created for {_renderer.FormatLocal(result.Event)} ({_config.TimeZone.Id}). {points} {link}", ephemeral: true);
    }

    [ManagerOnly]
    [SlashCommand("dfcancelevent", "Cancel an event")]
    public async Task CancelEventAsync(
        [Summary("event", "The event to cancel"), Autocomplete(typeof(ActiveEventAutocomplete))] string eventId)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        var error = await _events.CancelEventAsync(messageId, Context.User.Id);
        await FollowupAsync(error is null ? "✅ Event cancelled." : $"❌ {error}", ephemeral: true);
    }

    [ManagerOnly]
    [SlashCommand("dfcompleteevent", "Complete an event and pay points for killed bosses")]
    public async Task CompleteEventAsync(
        [Summary("event", "The open event to complete"), Autocomplete(typeof(OpenEventAutocomplete))] string eventId)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        var outcome = await _events.CompleteEventAsync(messageId, Context.User.Id);
        var prefix = outcome.Ok ? "" : "❌ ";
        foreach (var message in outcome.Messages)
            await FollowupAsync(prefix + message, ephemeral: true);
    }

    [ManagerOnly]
    [SlashCommand("dfbosskill", "Record a boss kill on an open points raid")]
    public async Task BossKillAsync(
        [Summary("event", "Open points event"), Autocomplete(typeof(OpenPointsEventAutocomplete))] string eventId,
        [Summary("boss", "Boss that was killed"), Autocomplete(typeof(RemainingBossAutocomplete))] string boss)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        await ReplyAsync(await _events.RecordBossKillAsync(messageId, boss, Context.User.Id));
    }

    [ManagerOnly]
    [SlashCommand("dfundobosskill", "Remove a recorded boss kill from an open points raid")]
    public async Task UndoBossKillAsync(
        [Summary("event", "Open points event"), Autocomplete(typeof(OpenPointsEventAutocomplete))] string eventId,
        [Summary("boss", "Recorded kill to remove"), Autocomplete(typeof(KilledBossAutocomplete))] string boss)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        await ReplyAsync(await _events.UndoBossKillAsync(messageId, boss, Context.User.Id));
    }

    [ManagerOnly]
    [SlashCommand("dfeditkill", "Add or remove a player on one recorded boss kill")]
    public async Task EditKillAsync(
        [Summary("event", "Open points event"), Autocomplete(typeof(OpenPointsEventAutocomplete))] string eventId,
        [Summary("boss", "Recorded boss kill"), Autocomplete(typeof(KilledBossAutocomplete))] string boss,
        [Summary("user", "Player to add or remove")] IUser user,
        [Summary("action", "add or remove"), Choice("add", "add"), Choice("remove", "remove")] string action)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        var add = action.Equals("add", StringComparison.OrdinalIgnoreCase);
        if (!add && !action.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            await FollowupAsync("❌ Pick add or remove.", ephemeral: true);
            return;
        }

        await ReplyAsync(await _events.EditKillAsync(messageId, boss, user.Id, add, Context.User.Id));
    }

    [ManagerOnly]
    [SlashCommand("dfplayerleft", "Exclude a player from future boss-kill points")]
    public async Task PlayerLeftAsync(
        [Summary("event", "Open points event"), Autocomplete(typeof(OpenPointsEventAutocomplete))] string eventId,
        [Summary("user", "Player who left")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        await ReplyAsync(await _events.ExcludePlayerAsync(messageId, user.Id, Context.User.Id));
    }

    [ManagerOnly]
    [SlashCommand("dfplayerjoined", "Include a player in future boss-kill points again")]
    public async Task PlayerJoinedAsync(
        [Summary("event", "Open points event"), Autocomplete(typeof(OpenPointsEventAutocomplete))] string eventId,
        [Summary("user", "Player who joined")] IUser user)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;
        if (await TryEventIdAsync(eventId) is not ulong messageId)
            return;

        await ReplyAsync(await _events.IncludePlayerAsync(messageId, user.Id, Context.User.Id));
    }

    [ManagerOnly]
    [SlashCommand("dflistevents", "List the active events")]
    public async Task ListEventsAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;

        var events = await _events.ListEventsAsync();
        var open = events.Count(e => e.Status == EventStatus.Open);
        var counter = OpenEventLimit.Counter(open, _config.Global.MaxOpenEvents);
        if (events.Count == 0)
        {
            await FollowupAsync($"There are no active events (**{counter}**).", ephemeral: true);
            return;
        }

        var lines = events.Select(e =>
        {
            var status = e.Status == EventStatus.Completed ? "completed" : "open";
            var points = e.AwardsPoints ? "points" : "no points";
            return $"• **{e.DisplayName}** – {_renderer.FormatLocal(e)} – {SignupLogic.CountAttending(e)}/{e.AttendiesRequired} – {status} – {points} – " +
                   DiscordHelpers.MessageLink(e.GuildId, EventMessagePolicy.LinkChannelId(e), e.MessageId);
        });
        var text = $"**{counter}**\n" + string.Join('\n', lines);
        if (text.Length > 1900)
            text = text[..1900] + "…";
        await FollowupAsync(text, ephemeral: true);
    }

    [ManagerOnly]
    [SlashCommand("dflisteventtemplates", "List the available event templates")]
    public Task ListEventTemplatesAsync() => ReplyWithTemplatesAsync(null);

    [ManagerOnly]
    [SlashCommand("dflistraidtemplates", "List the available raid templates")]
    public Task ListRaidTemplatesAsync() => ReplyWithTemplatesAsync(EventType.Raid);

    [ManagerOnly]
    [SlashCommand("dflistdungeontemplates", "List the available dungeon templates")]
    public Task ListDungeonTemplatesAsync() => ReplyWithTemplatesAsync(EventType.Dungeon);

    [ManagerOnly]
    [SlashCommand("dfhelp", "List Dungeon Forge commands and their parameters")]
    public async Task HelpAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;

        var messages = CommandHelpFormatter.Format(
            _interactions.SlashCommands,
            _config.Global.Roles.Manager.Name,
            _config.Global.Channels.Manage.Name,
            _config.Global.Channels.Events.Name);
        foreach (var message in messages)
            await FollowupAsync(message, ephemeral: true);
    }

    [SlashCommand("dfpoints", "Show points for you or another player")]
    public async Task PointsAsync(
        [Summary("user", "Whose points to show (default: you)")] IUser? user = null)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.DungeoneerOrManager)) return;

        foreach (var message in await _events.PointsForAsync(user?.Id ?? Context.User.Id))
            await FollowupAsync(message, ephemeral: true);
    }

    [SlashCommand("dfleaderboard", "Show every player's points")]
    public async Task LeaderboardAsync()
    {
        if (!await CheckAccessAsync(CommandAccess.DungeoneerOrManager)) return;

        var messages = await _events.LeaderboardAsync();
        if (messages.Count == 0)
        {
            await RespondAsync("No points have been awarded yet.", ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await RespondAsync(messages[0], ephemeral: true, allowedMentions: AllowedMentions.None);
        for (var i = 1; i < messages.Count; i++)
            await FollowupAsync(messages[i], ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    // ---------------------------------------------------------------- helpers

    private readonly record struct EventDefinitionRepositoryResult(EventDefinition? Definition, string? Error);

    private enum CommandAccess
    {
        Manager,
        DungeoneerOrManager,
    }

    private async Task ReplyWithTemplatesAsync(EventType? type)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync(CommandAccess.Manager)) return;

        var (valid, errors) = _definitions.LoadAll();
        foreach (var message in EventTemplateListFormatter.Format(valid, errors, type: type))
            await FollowupAsync(message, ephemeral: true);
    }

    private async Task<ulong?> TryEventIdAsync(string eventId)
    {
        if (ulong.TryParse(eventId, out var messageId))
            return messageId;

        await FollowupAsync("❌ Please pick an event from the suggestions.", ephemeral: true);
        return null;
    }

    private async Task ReplyAsync(CommandOutcome outcome) =>
        await FollowupAsync((outcome.Ok ? "✅ " : "❌ ") + outcome.Message, ephemeral: true);

    private EventDefinitionRepositoryResult LoadDefinition(string name)
    {
        try
        {
            return new(_definitions.Load(name), null);
        }
        catch (ConfigurationException ex)
        {
            return new(null, string.Join("\n", ex.Errors));
        }
    }

    /// <summary>Checks manage channel and role; answers the deferred interaction when access is denied.</summary>
    private async Task<bool> CheckAccessAsync(CommandAccess access)
    {
        if (Context.Channel.Id != _channels.Manage)
        {
            await ReplyEphemeralAsync($"❌ This command only works in <#{_channels.Manage}>.");
            return false;
        }

        var user = Context.User as IGuildUser;
        var manager = user is not null && DiscordHelpers.IsInRole(user, Context.Guild, _config.Global.Roles.Manager);
        var dungeoneer = user is not null && DiscordHelpers.IsInRole(user, Context.Guild, _config.Global.Roles.Dungeoneer);
        var allowed = access == CommandAccess.Manager ? manager : manager || dungeoneer;
        if (allowed)
            return true;

        var needed = access == CommandAccess.Manager
            ? $"**{_config.Global.Roles.Manager.Name}**"
            : $"**{_config.Global.Roles.Dungeoneer.Name}** or **{_config.Global.Roles.Manager.Name}**";
        await ReplyEphemeralAsync($"❌ You need the {needed} role to use this command.");
        _log.Warn($"{EventRenderer.Mention(Context.User.Id)} tried to use /{(Context.Interaction as SocketSlashCommand)?.CommandName} without the required role.");
        return false;
    }

    /// <summary>Deferred commands follow up. /dfleaderboard answers with the interaction reply.</summary>
    private Task ReplyEphemeralAsync(string text)
    {
        if (Context.Interaction.HasResponded)
            return FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        return RespondAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}

/// <summary>Suggests templates from EventConfigurations and its subfolders. The value is the template name.</summary>
public sealed class EventFileAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var repo = services.GetRequiredService<EventDefinitionRepository>();
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var (valid, _) = repo.LoadAll();
        var suggestions = EventTemplateAutocomplete.Suggest(valid, typed)
            .Select(choice => new AutocompleteResult(choice.Label, choice.Value));
        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}

/// <summary>Suggests stored events, including completed ones (value = event message id).</summary>
public sealed class ActiveEventAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) =>
        EventSuggestion.EventsAsync(services, autocompleteInteraction, _ => true);
}

/// <summary>Suggests events that are still open.</summary>
public sealed class OpenEventAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) =>
        EventSuggestion.EventsAsync(services, autocompleteInteraction, e => e.Status == EventStatus.Open);
}

/// <summary>Suggests open events that award points.</summary>
public sealed class OpenPointsEventAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) =>
        EventSuggestion.EventsAsync(services, autocompleteInteraction, e => e.Status == EventStatus.Open && e.AwardsPoints);
}

/// <summary>Bosses of the selected raid that have not been recorded yet.</summary>
public sealed class RemainingBossAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) =>
        EventSuggestion.BossesAsync(services, autocompleteInteraction, killed: false);
}

/// <summary>Bosses already recorded on the selected event.</summary>
public sealed class KilledBossAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services) =>
        EventSuggestion.BossesAsync(services, autocompleteInteraction, killed: true);
}

internal static class EventSuggestion
{
    public static async Task<AutocompletionResult> EventsAsync(
        IServiceProvider services,
        IAutocompleteInteraction interaction,
        Func<EventInstance, bool> filter)
    {
        var events = await services.GetRequiredService<EventService>().ListEventsAsync();
        var renderer = services.GetRequiredService<EventRenderer>();
        var typed = interaction.Data.Current.Value?.ToString() ?? "";
        var suggestions = events
            .Where(filter)
            .Select(e =>
            {
                var status = e.Status == EventStatus.Completed ? "completed" : "open";
                return (Label: $"{e.DisplayName} – {renderer.FormatLocal(e)} ({status})", Id: e.MessageId);
            })
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(x.Label.Length <= 100 ? x.Label : x.Label[..100], x.Id.ToString()));
        return AutocompletionResult.FromSuccess(suggestions);
    }

    public static async Task<AutocompletionResult> BossesAsync(
        IServiceProvider services,
        IAutocompleteInteraction interaction,
        bool killed)
    {
        var typed = interaction.Data.Current.Value?.ToString() ?? "";
        if (!ulong.TryParse(Option(interaction, "event"), out var messageId))
            return AutocompletionResult.FromSuccess(Array.Empty<AutocompleteResult>());

        var events = await services.GetRequiredService<EventService>().ListEventsAsync();
        var e = events.FirstOrDefault(ev => ev.MessageId == messageId);
        if (e is null)
            return AutocompletionResult.FromSuccess(Array.Empty<AutocompleteResult>());

        IEnumerable<string> names;
        if (killed)
        {
            names = (e.Kills ?? new List<BossKillRecord>()).Select(kill => kill.Boss);
        }
        else
        {
            try
            {
                var def = services.GetRequiredService<EventDefinitionRepository>().Load(e.EventName);
                var killedNames = (e.Kills ?? new List<BossKillRecord>()).Select(kill => kill.Boss)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                names = (def.Bosses ?? new List<BossDefinition>()).Select(boss => boss.Name)
                    .Where(name => !killedNames.Contains(name));
            }
            catch (ConfigurationException)
            {
                names = Array.Empty<string>();
            }
        }

        var suggestions = names
            .Where(name => name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(name => new AutocompleteResult(name.Length <= 100 ? name : name[..100], name));
        return AutocompletionResult.FromSuccess(suggestions);
    }

    private static string Option(IAutocompleteInteraction interaction, string name)
    {
        foreach (var option in interaction.Data.Options)
        {
            if (option.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return option.Value?.ToString() ?? "";
        }

        return "";
    }
}
