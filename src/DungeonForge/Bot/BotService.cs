using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DungeonForge.Configuration;
using DungeonForge.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Bot;

/// <summary>Owns the Discord connection: login, gateway event wiring, startup (commands, reconciliation) and shutdown.</summary>
public sealed class BotService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly IEventStore _store;
    private readonly IPointsStore _points;
    private readonly IPointsChannelStore _pointsChannel;
    private readonly PointsBoardService _pointsBoard;
    private readonly EventService _events;
    private readonly EventWorkQueue _queue;
    private readonly IBotLog _log;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<BotService> _logger;

    private int _initStarted;
    private int _strayDeleteWarned;

    public BotService(DiscordSocketClient client, InteractionService interactions, IServiceProvider services,
        BotConfiguration config, ResolvedChannels channels, IEventStore store, IPointsStore points,
        IPointsChannelStore pointsChannel, PointsBoardService pointsBoard, EventService events,
        EventWorkQueue queue, IBotLog log, IHostApplicationLifetime lifetime, ILogger<BotService> logger)
    {
        _client = client;
        _interactions = interactions;
        _services = services;
        _config = config;
        _channels = channels;
        _store = store;
        _points = points;
        _pointsChannel = pointsChannel;
        _pointsBoard = pointsBoard;
        _events = events;
        _queue = queue;
        _log = log;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _store.LoadAsync();
            await _points.LoadAsync();
            await _pointsChannel.LoadAsync();
            _logger.LogInformation("Loaded {Count} stored event(s) and {Points} points entry(s).", _store.Events.Count, _points.Entries.Count);

            var (valid, errors) = new EventDefinitionRepository(_config).LoadAll();
            _logger.LogInformation("Found {Count} valid event file(s) in EventConfigurations.", valid.Count);
            foreach (var error in errors)
                _logger.LogWarning("Invalid event file: {Error}", error);

            _client.Log += OnDiscordLog;
            _interactions.Log += OnDiscordLog;
            _client.Ready += OnReady;
            _client.InteractionCreated += OnInteractionCreated;
            _client.ReactionAdded += (message, channel, reaction) =>
            {
                _queue.Enqueue("reaction added", () =>
                    _events.HandleReactionAsync(channel.Id, message.Id, reaction.UserId, reaction.Emote, added: true));
                return Task.CompletedTask;
            };
            _client.ReactionRemoved += (message, channel, reaction) =>
            {
                _queue.Enqueue("reaction removed", () =>
                    _events.HandleReactionAsync(channel.Id, message.Id, reaction.UserId, reaction.Emote, added: false));
                return Task.CompletedTask;
            };
            _client.MessageDeleted += (message, channel) =>
            {
                _queue.Enqueue("message deleted", () => _events.HandleMessageDeletedAsync(channel.Id, message.Id));
                return Task.CompletedTask;
            };
            _client.MessageReceived += OnMessageReceived;
            _interactions.SlashCommandExecuted += OnSlashCommandExecuted;

            await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);

            try
            {
                await _client.LoginAsync(TokenType.Bot, _config.Global.Token);
                await _client.StartAsync();
            }
            catch (Exception ex)
            {
                Fail($"Could not log in to Discord: {ex.Message}. Check the bot token in global.json / {ConfigurationLoader.TokenEnvironmentVariable}.");
                return;
            }

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            try { await _client.StopAsync(); } catch { /* ignore */ }
        }
    }

    private void Fail(string message)
    {
        _logger.LogCritical("{Message}", message);
        Environment.ExitCode = 1;
        _lifetime.StopApplication();
    }

    private Task OnReady()
    {
        // Ready can fire again after a full reconnect; initialize only once. Run off the gateway thread.
        if (Interlocked.Exchange(ref _initStarted, 1) == 0)
            _ = Task.Run(InitializeAsync);
        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var global = _config.Global;
            var guild = _client.GetGuild(global.GuildId);
            if (guild is null)
            {
                Fail($"The bot is not a member of the server with guildId {global.GuildId}. Invite it with the invite URL from the README and check \"guildId\" in global.json.");
                return;
            }

            // resolve channels
            var errors = new List<string>();
            var events = ResolveChannel(guild, global.Channels.Events, "events", errors);
            var manage = ResolveChannel(guild, global.Channels.Manage, "manage", errors);
            var log = ResolveChannel(guild, global.Channels.Log, "log", errors);
            if (errors.Count > 0)
            {
                foreach (var error in errors) _logger.LogCritical("{Error}", error);
                Fail("Channel configuration is invalid (see above). The bot can only see channels it has View Channel permission for.");
                return;
            }
            var points = ResolvePointsChannel(guild, global.Channels.Points);
            _channels.SetChannels(events, manage, log, points);

            // warn about missing Discord roles
            foreach (var (label, role) in new[]
                     {
                         ("dungeoneer", global.Roles.Dungeoneer), ("manager", global.Roles.Manager), ("logs", global.Roles.Logs),
                     })
            {
                if (!DiscordHelpers.RoleExists(guild, role))
                    _log.Warn($"The Discord role for \"{label}\" ({role}) does not exist on the server. Create it or fix global.json.");
            }

            await _interactions.RegisterCommandsToGuildAsync(global.GuildId, deleteMissing: true);

            await _events.ReconcileAllAsync();
            await _pointsBoard.RefreshBossTableAsync();
            await _pointsBoard.RefreshLeaderboardAsync();

            _log.Info($"Dungeon Forge is online as **{_client.CurrentUser.Username}** on **{guild.Name}**. " +
                      $"{_store.Events.Count} active event(s). Time zone: {_config.TimeZone.Id}.");
            _channels.MarkInitialized();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Startup failed.");
            Fail("Startup failed: " + ex.Message);
        }
    }

    private static ulong ResolveChannel(SocketGuild guild, NamedId configured, string label, List<string> errors)
    {
        if (configured.Id != 0)
        {
            if (guild.GetTextChannel(configured.Id) is { } byId) return byId.Id;
            errors.Add($"channels.{label}: no text channel with id {configured.Id} is visible to the bot.");
            return 0;
        }

        var byName = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(configured.Name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null) return byName.Id;
        errors.Add($"channels.{label}: no text channel named '{configured.Name}' was found (set its \"id\" in global.json).");
        return 0;
    }

    /// <summary>The points channel is optional at runtime: a missing or placeholder channel is logged and skipped.</summary>
    private ulong ResolvePointsChannel(SocketGuild guild, NamedId? configured)
    {
        if (configured is null || IsPlaceholder(configured))
        {
            _log.Warn("The points channel is not configured. Set channels.points in global.json (default name dungeon-forge-points). The points board will not be posted.");
            return 0;
        }

        if (configured.Id != 0)
        {
            if (guild.GetTextChannel(configured.Id) is { } byId)
                return byId.Id;
            _log.Warn($"channels.points: no text channel with id {configured.Id} is visible to the bot. The points board will not be posted.");
            return 0;
        }

        var byName = guild.TextChannels.FirstOrDefault(c => c.Name.Equals(configured.Name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
            return byName.Id;

        _log.Warn($"channels.points: no text channel named '{configured.Name}' was found. Create it or set its id. The points board will not be posted.");
        return 0;
    }

    private static bool IsPlaceholder(NamedId configured) =>
        configured.Id == 0 && (string.IsNullOrWhiteSpace(configured.Name) ||
                               configured.Name.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase));

    private async Task OnInteractionCreated(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction failed.");
        }
    }

    private async Task OnSlashCommandExecuted(SlashCommandInfo command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess) return;

        _logger.LogWarning("Slash command /{Command} failed: {Error} ({Reason})", command?.Name, result.Error, result.ErrorReason);
        const string text = "❌ Something went wrong while running that command. Please try again or check the log.";
        try
        {
            if (context.Interaction.HasResponded)
                await context.Interaction.FollowupAsync(text, ephemeral: true);
            else
                await context.Interaction.RespondAsync(text, ephemeral: true);
        }
        catch
        {
            // interaction may have expired
        }
    }

    private async Task OnMessageReceived(SocketMessage message)
    {
        // Needs no MessageContent intent: author and channel are always known.
        if (message.Channel.Id == _channels.Events && _channels.Events != 0)
        {
            if (EventMessagePolicy.DeleteParentMessage(
                    _config.Global.DeleteStrayMessages,
                    message.Source == MessageSource.User,
                    message.Type == MessageType.ThreadCreated,
                    message.Author.IsBot))
                await TryDeleteStrayAsync(message, "events channel");
            return;
        }

        if (message.Channel is SocketThreadChannel &&
            EventMessagePolicy.DeleteThreadMessage(
                _config.Global.DeleteStrayThreadMessages,
                await _events.IsEventThreadAsync(message.Channel.Id),
                message.Source == MessageSource.User,
                message.Author.IsBot))
            await TryDeleteStrayAsync(message, "event thread");
    }

    private async Task TryDeleteStrayAsync(SocketMessage message, string where)
    {
        try
        {
            await message.DeleteAsync();
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _strayDeleteWarned, 1) == 0)
                _log.Warn($"Could not delete a stray message in the {where} (the bot needs Manage Messages): {ex.Message}");
        }
    }

    private Task OnDiscordLog(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

        // Keep warnings short (no stack traces); errors keep the full exception.
        if (message.Exception is not null && level >= LogLevel.Error)
            _logger.Log(level, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        else
            _logger.Log(level, "[{Source}] {Message}{Reason}", message.Source, message.Message,
                message.Exception is null ? "" : " - " + FirstLine(message.Exception.Message));

        // Discord.Net retries forever on fatal authentication problems; stop with a clear message instead.
        for (var ex = message.Exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is Discord.Net.HttpException { HttpCode: System.Net.HttpStatusCode.Unauthorized })
            {
                Fail($"Discord rejected the bot token (401 Unauthorized). Check the token in global.json / {ConfigurationLoader.TokenEnvironmentVariable} (reset it in the developer portal if needed).");
                break;
            }
            if (ex is Discord.Net.WebSocketClosedException { CloseCode: 4004 })
            {
                Fail("Discord rejected the bot token (gateway close code 4004). Check the token.");
                break;
            }
            if (ex is Discord.Net.WebSocketClosedException { CloseCode: 4013 or 4014 })
            {
                Fail("Discord refused the requested gateway intents. Enable \"Server Members Intent\" for the bot in the Discord developer portal (Bot -> Privileged Gateway Intents).");
                break;
            }
        }
        return Task.CompletedTask;
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOfAny(new[] { '\r', '\n' });
        return i < 0 ? text : text[..i];
    }
}
