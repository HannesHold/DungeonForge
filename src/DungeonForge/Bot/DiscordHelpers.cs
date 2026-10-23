using Discord;
using Discord.Net;
using Discord.WebSocket;
using DungeonForge.Configuration;

namespace DungeonForge.Bot;

public static class DiscordHelpers
{
    /// <summary>True if the member has the configured role (matched by id when set, otherwise by name, case-insensitive).</summary>
    public static bool IsInRole(IGuildUser user, IGuild guild, NamedId role)
    {
        if (role.Id != 0)
            return user.RoleIds.Contains(role.Id);

        foreach (var id in user.RoleIds)
        {
            var r = guild.GetRole(id);
            if (r is not null && r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool RoleExists(IGuild guild, NamedId role) =>
        role.Id != 0
            ? guild.GetRole(role.Id) is not null
            : guild.Roles.Any(r => r.Name.Equals(role.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Gets a guild member from the cache, falling back to a REST request.</summary>
    public static async Task<IGuildUser?> GetGuildUserAsync(DiscordSocketClient client, ulong guildId, ulong userId)
    {
        var cached = client.GetGuild(guildId)?.GetUser(userId);
        if (cached is not null) return cached;

        try
        {
            return await client.Rest.GetGuildUserAsync(guildId, userId);
        }
        catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public static bool IsNotFound(Exception ex) =>
        ex is HttpException { HttpCode: System.Net.HttpStatusCode.NotFound };

    public static string MessageLink(ulong guildId, ulong channelId, ulong messageId) =>
        $"https://discord.com/channels/{guildId}/{channelId}/{messageId}";
}
