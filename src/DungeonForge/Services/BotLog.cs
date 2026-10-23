using System.Threading.Channels;
using Discord;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

public interface IBotLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Logs to the console (ILogger) and mirrors messages to the dungeon-forge-log channel.
/// Messages are queued, so logging never blocks gateway handlers; mentions never ping.
/// </summary>
public sealed class BotLog : BackgroundService, IBotLog
{
    private const int MaxLength = 1900;

    private readonly ILogger<BotLog> _logger;
    private readonly DiscordSocketClient _client;
    private readonly BotConfiguration _config;
    private readonly ResolvedChannels _channels;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    public BotLog(ILogger<BotLog> logger, DiscordSocketClient client, BotConfiguration config, ResolvedChannels channels)
    {
        _logger = logger;
        _client = client;
        _config = config;
        _channels = channels;
    }

    public void Info(string message)
    {
        _logger.LogInformation("{Message}", message);
        Enqueue("ℹ️ " + message);
    }

    public void Warn(string message)
    {
        _logger.LogWarning("{Message}", message);
        Enqueue("⚠️ " + message);
    }

    public void Error(string message, Exception? exception = null)
    {
        _logger.LogError(exception, "{Message}", message);
        Enqueue("❌ " + message + (exception is null ? "" : $" ({exception.GetType().Name}: {exception.Message})"));
    }

    private void Enqueue(string text)
    {
        if (!_config.Global.LogToDiscord) return;
        _queue.Writer.TryWrite(text.Length <= MaxLength ? text : text[..MaxLength] + "…");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _channels.ChannelsReady.WaitAsync(stoppingToken);

            await foreach (var text in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var channel = _client.GetGuild(_config.Global.GuildId)?.GetTextChannel(_channels.Log);
                    if (channel is null) continue;
                    await channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Never log failures of the log channel back into the log channel.
                    _logger.LogWarning("Could not write to the Discord log channel: {Message}", ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
