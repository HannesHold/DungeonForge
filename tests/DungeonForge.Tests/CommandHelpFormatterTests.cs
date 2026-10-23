using Discord.Interactions;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DungeonForge.Tests;

public class CommandHelpFormatterTests
{
    private static readonly string[] ExpectedOrder =
    {
        "dfcreateevent",
        "dfcancelevent",
        "dfcompleteevent",
        "dfbosskill",
        "dfundobosskill",
        "dfeditkill",
        "dfplayerleft",
        "dfplayerjoined",
        "dflistevents",
        "dflisteventtemplates",
        "dflistraidtemplates",
        "dflistdungeontemplates",
        "dfpoints",
        "dfleaderboard",
        "dfhelp",
    };

    private static readonly HashSet<string> DungeoneerCommands = new(StringComparer.Ordinal)
    {
        "dfpoints",
        "dfleaderboard",
    };

    [Fact]
    public async Task Help_ListsEveryRegisteredSlashCommand_WithParametersAndManagerMarker()
    {
        await using var loaded = await LoadedCommands.CreateAsync();
        var commands = loaded.Service.SlashCommands;
        var messages = CommandHelpFormatter.Format(commands);
        var text = string.Join('\n', messages);
        var bullets = text.Split('\n').Where(line => line.StartsWith("• **/", StringComparison.Ordinal)).ToList();

        foreach (var name in ExpectedOrder)
            Assert.Contains(commands, command => command.Name == name);
        Assert.Equal(commands.Count, bullets.Count);

        var previous = -1;
        foreach (var name in ExpectedOrder)
        {
            var index = text.IndexOf($"**/{name}**", StringComparison.Ordinal);
            Assert.True(index > previous, name);
            previous = index;
        }

        foreach (var command in commands)
        {
            var bullet = Assert.Single(bullets, line => line.Contains($"**/{command.Name}**", StringComparison.Ordinal));
            Assert.Contains(command.Description, bullet);
            if (DungeoneerCommands.Contains(command.Name))
            {
                Assert.DoesNotContain("manager-only", bullet);
                Assert.False(CommandHelpFormatter.IsManagerOnly(command));
            }
            else
            {
                Assert.Contains("manager-only", bullet);
                Assert.True(CommandHelpFormatter.IsManagerOnly(command));
            }
            foreach (var parameter in command.Parameters)
                Assert.Contains($"`{parameter.Name}`", bullet);
        }

        var create = Assert.Single(bullets, line => line.Contains("**/dfcreateevent**", StringComparison.Ordinal));
        Assert.Contains("`event`", create);
        Assert.Contains("`date`", create);
        Assert.Contains("`time`", create);
        Assert.Contains("`awardpoints`", create);
        Assert.Contains("df-manager", text);
        Assert.Contains("dungeon-forge-manage", text);
        Assert.Contains("dungeon-forge-forum", text);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, EventTemplateListFormatter.DiscordMessageLimit));
        Assert.All(messages, message => Assert.False(message.StartsWith('\n') || message.EndsWith('\n') || message.StartsWith(' ')));

        var custom = string.Join('\n', CommandHelpFormatter.Format(commands, "raid-lead", "raid-room"));
        Assert.Contains("**raid-lead**", custom);
        Assert.Contains("**raid-room**", custom);
    }

    [Fact]
    public async Task Help_ChunksStayWithinTheLimit()
    {
        await using var loaded = await LoadedCommands.CreateAsync();
        const int limit = 220;
        var messages = CommandHelpFormatter.Format(loaded.Service.SlashCommands, maxLength: limit);
        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, limit));
        Assert.All(messages, message => Assert.False(message.StartsWith('\n') || message.EndsWith('\n')));
        Assert.DoesNotContain(messages, message => message.EndsWith("**Dungeon Forge commands**", StringComparison.Ordinal));

        var text = string.Join('\n', messages);
        foreach (var command in loaded.Service.SlashCommands)
            Assert.Contains($"**/{command.Name}**", text);
    }

    private sealed class LoadedCommands : IAsyncDisposable
    {
        private readonly DiscordSocketClient _client;
        private readonly ServiceProvider _provider;

        private LoadedCommands(DiscordSocketClient client, ServiceProvider provider, InteractionService service)
        {
            _client = client;
            _provider = provider;
            Service = service;
        }

        public InteractionService Service { get; }

        public static async Task<LoadedCommands> CreateAsync()
        {
            var client = new DiscordSocketClient();
            var interactions = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Async });
            var root = Directory.CreateTempSubdirectory().FullName;
            var services = new ServiceCollection();
            services.AddSingleton(client);
            services.AddSingleton(interactions);
            services.AddSingleton(new BotConfiguration(new GlobalConfig(), TestSupport.Roles, TestSupport.FixedRoles, TimeZoneInfo.Utc, root));
            services.AddSingleton(new ResolvedChannels());
            services.AddSingleton<EventDefinitionRepository>();
            services.AddSingleton<EventRenderer>();
            services.AddSingleton<IEventStore>(_ => new EventStore(Path.Combine(root, "events.json")));
            services.AddSingleton<IPointsStore>(_ => new PointsStore(Path.Combine(root, "points.json")));
            services.AddSingleton<IPointsChannelStore>(_ => new PointsChannelStore(Path.Combine(root, "points-channel.json")));
            services.AddSingleton<IBotLog, SilentLog>();
            services.AddSingleton<PointsBoardService>();
            services.AddSingleton<EventService>();
            var provider = services.BuildServiceProvider();
            await interactions.AddModulesAsync(typeof(EventCommands).Assembly, provider);
            return new LoadedCommands(client, provider, interactions);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await _client.DisposeAsync();
        }
    }

    private sealed class SilentLog : IBotLog
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
