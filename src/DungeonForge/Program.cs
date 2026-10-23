using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using DungeonForge.Bot;
using DungeonForge.Configuration;
using DungeonForge.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Dungeon Forge - Discord raid/dungeon organizer.
// Configuration is read from the "Configurations" folder next to the executable.

BotConfiguration config;
try
{
    config = ConfigurationLoader.Load(AppContext.BaseDirectory);
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine("Dungeon Forge cannot start - please fix the configuration:");
    foreach (var error in ex.Errors)
        Console.Error.WriteLine("  - " + error);
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Configuration folder: {Path.Combine(AppContext.BaseDirectory, "Configurations")}");
    Console.Error.WriteLine("(When started from Visual Studio / dotnet run, edit the files in the project's Configurations folder and rebuild/restart.)");
    return 1;
}

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

var services = builder.Services;
services.AddSingleton(config);
services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
    // Privileged: GuildMembers (enable "Server Members Intent" in the developer portal).
    // MessageContent is NOT needed.
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers |
                     GatewayIntents.GuildMessages | GatewayIntents.GuildMessageReactions,
    AlwaysDownloadUsers = true,
    LogLevel = LogSeverity.Info,
}));
services.AddSingleton(sp => new InteractionService(sp.GetRequiredService<DiscordSocketClient>(),
    new InteractionServiceConfig { DefaultRunMode = RunMode.Async, LogLevel = LogSeverity.Info }));

services.AddSingleton<ResolvedChannels>();
services.AddSingleton<IEventStore, EventStore>();
services.AddSingleton<EventDefinitionRepository>();
services.AddSingleton<EventRenderer>();
services.AddSingleton<EventService>();

services.AddSingleton<BotLog>();
services.AddSingleton<IBotLog>(sp => sp.GetRequiredService<BotLog>());
services.AddHostedService(sp => sp.GetRequiredService<BotLog>());

services.AddSingleton<EventWorkQueue>();
services.AddHostedService(sp => sp.GetRequiredService<EventWorkQueue>());

services.AddSingleton<BotService>();
services.AddHostedService(sp => sp.GetRequiredService<BotService>());

using var host = builder.Build();
await host.RunAsync();
return Environment.ExitCode;
