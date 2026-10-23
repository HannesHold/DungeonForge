namespace DungeonForge.Bot;

/// <summary>Channel ids after resolution (configured id, or looked up by name at startup) plus startup gates.</summary>
public sealed class ResolvedChannels
{
    private readonly TaskCompletionSource _channelsReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ulong Events { get; private set; }
    public ulong Manage { get; private set; }
    public ulong Log { get; private set; }

    /// <summary>Completes when the channel ids are known (the Discord log can start sending).</summary>
    public Task ChannelsReady => _channelsReady.Task;

    /// <summary>Completes when startup (command registration + reconciliation) has finished.</summary>
    public Task Initialized => _initialized.Task;

    public void SetChannels(ulong events, ulong manage, ulong log)
    {
        Events = events;
        Manage = manage;
        Log = log;
        _channelsReady.TrySetResult();
    }

    public void MarkInitialized() => _initialized.TrySetResult();
}
