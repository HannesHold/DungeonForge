using Discord.Interactions;
using DungeonForge.Services;

namespace DungeonForge.Bot;

/// <summary>
/// Builds the ephemeral /dfhelp reply from the slash commands registered on <see cref="InteractionService"/>,
/// so a new command shows up here without a second list to maintain.
/// </summary>
public static class CommandHelpFormatter
{
    public const int DiscordMessageLimit = EventTemplateListFormatter.DiscordMessageLimit;

    private static readonly string[] PreferredOrder =
    [
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
    ];

    public static IReadOnlyList<string> Format(
        IEnumerable<SlashCommandInfo> commands,
        string managerRole = "df-manager",
        string manageChannel = "dungeon-forge-manage",
        string eventsChannel = "dungeon-forge-forum",
        int maxLength = DiscordMessageLimit)
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Message length limit must be positive.");

        var ordered = commands
            .OrderBy(Rank)
            .ThenBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count == 0)
            return new[] { "There are no commands." };

        var lines = new List<string>
        {
            "**Dungeon Forge commands**",
            $"Usable in **{manageChannel}**. Events go in **{eventsChannel}** (text or forum). **manager-only** needs **{managerRole}**. dfpoints and dfleaderboard also work with the dungeoneer role.",
            "",
        };
        foreach (var command in ordered)
            lines.Add(FormatCommand(command));

        var messages = DiscordMessagePacker.Pack(lines, maxLength);
        return messages.Count == 0 ? new[] { "There are no commands." } : messages;
    }

    public static bool IsManagerOnly(SlashCommandInfo command) =>
        command.Attributes.OfType<ManagerOnlyAttribute>().Any()
        || command.Module.Attributes.OfType<ManagerOnlyAttribute>().Any();

    /// <summary>Slash path such as <c>dfhelp</c>, or <c>group name</c> when the command is in a group.</summary>
    public static string CommandPath(SlashCommandInfo command)
    {
        var names = new List<string>();
        var module = command.Module;
        while (module is not null)
        {
            if (!string.IsNullOrEmpty(module.SlashGroupName))
                names.Add(module.SlashGroupName);
            module = module.Parent;
        }

        names.Reverse();
        names.Add(command.Name);
        return string.Join(' ', names);
    }

    private static string FormatCommand(SlashCommandInfo command)
    {
        var parameters = command.Parameters.Count == 0
            ? ""
            : " " + string.Join(' ', command.Parameters.Select(parameter => $"`{parameter.Name}`"));
        var description = string.IsNullOrWhiteSpace(command.Description)
            ? "No description."
            : DiscordMessagePacker.OneLine(command.Description);
        var marker = IsManagerOnly(command) ? " (manager-only)" : "";
        return $"• **/{CommandPath(command)}**{parameters} – {description}{marker}";
    }

    private static int Rank(SlashCommandInfo command)
    {
        var index = Array.FindIndex(PreferredOrder, name => name.Equals(command.Name, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? PreferredOrder.Length : index;
    }
}
