using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DungeonForge.Configuration;
using DungeonForge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Bot;

/// <summary>Manager slash commands. Only usable in the manage channel by members of the manager role.</summary>
[RequireContext(ContextType.Guild)]
public sealed class EventCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly EventDefinitionRepository _definitions;
    private readonly EventService _events;
    private readonly EventRenderer _renderer;
    private readonly IBotLog _log;

    public EventCommands(BotConfiguration config, ResolvedChannels channels, EventDefinitionRepository definitions,
        EventService events, EventRenderer renderer, IBotLog log)
    {
        _config = config;
        _channels = channels;
        _definitions = definitions;
        _events = events;
        _renderer = renderer;
        _log = log;
    }

    [SlashCommand("dfcreateevent", "Create a dungeon/raid event, e.g. /dfcreateevent MoltenCore40 24.10.2026 18:00")]
    public async Task CreateEventAsync(
        [Summary("event", "Name of the event file in EventConfigurations"), Autocomplete(typeof(EventFileAutocomplete))] string eventName,
        [Summary("date", "Date as dd.MM.yyyy, e.g. 24.10.2026")] string date,
        [Summary("time", "Time as HH:mm (24h), e.g. 18:00")] string time)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync()) return;

        EventDefinitionRepositoryResult load = LoadDefinition(eventName);
        if (load.Error is not null)
        {
            await FollowupAsync($"❌ {load.Error}", ephemeral: true);
            return;
        }

        if (!EventDateParser.TryParse(date, time, _config.TimeZone, DateTimeOffset.UtcNow, out var start, out var error))
        {
            await FollowupAsync($"❌ {error}", ephemeral: true);
            return;
        }

        var result = await _events.CreateEventAsync(load.Definition!, start, Context.User.Id);
        if (result.Event is null)
        {
            await FollowupAsync($"❌ {result.Error}", ephemeral: true);
            return;
        }

        var link = DiscordHelpers.MessageLink(Context.Guild.Id, result.Event.ChannelId, result.Event.MessageId);
        await FollowupAsync($"✅ Event **{result.Event.DisplayName}** created for {_renderer.FormatLocal(result.Event)} ({_config.TimeZone.Id}): {link}", ephemeral: true);
    }

    [SlashCommand("dfcancelevent", "Cancel an event")]
    public async Task CancelEventAsync(
        [Summary("event", "The event to cancel"), Autocomplete(typeof(ActiveEventAutocomplete))] string eventId)
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync()) return;

        if (!ulong.TryParse(eventId, out var messageId))
        {
            await FollowupAsync("❌ Please pick an event from the suggestions.", ephemeral: true);
            return;
        }

        var error = await _events.CancelEventAsync(messageId, Context.User.Id);
        await FollowupAsync(error is null ? "✅ Event cancelled." : $"❌ {error}", ephemeral: true);
    }

    [SlashCommand("dflistevents", "List the active events")]
    public async Task ListEventsAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync()) return;

        var events = await _events.ListEventsAsync();
        if (events.Count == 0)
        {
            await FollowupAsync("There are no active events.", ephemeral: true);
            return;
        }

        var lines = events.Select(e =>
            $"• **{e.DisplayName}** – {_renderer.FormatLocal(e)} – {SignupLogic.CountAttending(e)}/{e.AttendiesRequired} – " +
            DiscordHelpers.MessageLink(e.GuildId, e.ChannelId, e.MessageId));
        var text = string.Join('\n', lines);
        if (text.Length > 1900) text = text[..1900] + "…";
        await FollowupAsync(text, ephemeral: true);
    }

    [SlashCommand("dflisteventtemplates", "List the available event templates")]
    public async Task ListEventTemplatesAsync()
    {
        await DeferAsync(ephemeral: true);
        if (!await CheckAccessAsync()) return;

        var (valid, errors) = _definitions.LoadAll();
        foreach (var message in EventTemplateListFormatter.Format(valid, errors))
            await FollowupAsync(message, ephemeral: true);
    }

    // ---------------------------------------------------------------- helpers

    private readonly record struct EventDefinitionRepositoryResult(Models.EventDefinition? Definition, string? Error);

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

    /// <summary>Checks manage channel + manager role; answers the (deferred) interaction with an error if not allowed.</summary>
    private async Task<bool> CheckAccessAsync()
    {
        if (Context.Channel.Id != _channels.Manage)
        {
            await FollowupAsync($"❌ This command only works in <#{_channels.Manage}>.", ephemeral: true);
            return false;
        }

        if (Context.User is not IGuildUser user || !DiscordHelpers.IsInRole(user, Context.Guild, _config.Global.Roles.Manager))
        {
            await FollowupAsync($"❌ You need the **{_config.Global.Roles.Manager.Name}** role to use this command.", ephemeral: true);
            _log.Warn($"{EventRenderer.Mention(Context.User.Id)} tried to use /{(Context.Interaction as SocketSlashCommand)?.CommandName} without the manager role.");
            return false;
        }

        return true;
    }
}

/// <summary>Suggests the names of the files in EventConfigurations.</summary>
public sealed class EventFileAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var repo = services.GetRequiredService<EventDefinitionRepository>();
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var suggestions = repo.ListNames()
            .Where(n => n.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(n => new AutocompleteResult(n, n));
        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}

/// <summary>Suggests the active events (value = event message id).</summary>
public sealed class ActiveEventAutocomplete : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var events = await services.GetRequiredService<EventService>().ListEventsAsync();
        var renderer = services.GetRequiredService<EventRenderer>();
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var suggestions = events
            .Select(e => (Label: $"{e.DisplayName} – {renderer.FormatLocal(e)}", Id: e.MessageId))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(x.Label.Length <= 100 ? x.Label : x.Label[..100], x.Id.ToString()));
        return AutocompletionResult.FromSuccess(suggestions);
    }
}
