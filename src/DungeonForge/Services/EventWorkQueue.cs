using System.Threading.Channels;
using DungeonForge.Bot;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DungeonForge.Services;

/// <summary>
/// Runs gateway-triggered work (reactions, deleted messages, deleted threads) strictly one after another, in arrival order,
/// off the gateway thread. Work waits until startup reconciliation has finished.
/// </summary>
public sealed class EventWorkQueue : BackgroundService
{
    private readonly Channel<(string Description, Func<Task> Work)> _queue =
        Channel.CreateUnbounded<(string, Func<Task>)>(new UnboundedChannelOptions { SingleReader = true });

    private readonly ResolvedChannels _channels;
    private readonly ILogger<EventWorkQueue> _logger;

    public EventWorkQueue(ResolvedChannels channels, ILogger<EventWorkQueue> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public void Enqueue(string description, Func<Task> work) => _queue.Writer.TryWrite((description, work));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _channels.Initialized.WaitAsync(stoppingToken);
            await foreach (var (description, work) in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await work(); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Work item failed: {Description}", description); }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
