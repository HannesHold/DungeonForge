using System.Text;
using Discord;

namespace DungeonForge.Bot;

/// <summary>
/// Helpers to compare emoji robustly. Discord is inconsistent about the unicode variation selector
/// (U+FE0F), so "🛡️" and "🛡" must be treated as the same emoji.
/// </summary>
public static class EmojiKey
{
    /// <summary>Normalized key for an emoji string from the configuration (unicode or &lt;:name:id&gt; markup).</summary>
    public static string FromConfigured(string configured)
    {
        var trimmed = (configured ?? "").Trim();
        if (Emote.TryParse(trimmed, out var custom))
            return "custom:" + custom.Id;
        return "unicode:" + StripVariationSelectors(trimmed);
    }

    /// <summary>Normalized key for the emoji of a reaction.</summary>
    public static string FromReaction(IEmote emote) => emote switch
    {
        Emote custom => "custom:" + custom.Id,
        _ => "unicode:" + StripVariationSelectors(emote.Name ?? ""),
    };

    /// <summary>Converts a configured emoji string into something Discord.Net can react with.</summary>
    public static IEmote ToEmote(string configured)
    {
        var trimmed = configured.Trim();
        return Emote.TryParse(trimmed, out var custom) ? custom : new Emoji(trimmed);
    }

    private static string StripVariationSelectors(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch is '\uFE0F' or '\uFE0E') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
