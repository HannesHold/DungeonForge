namespace DungeonForge.Services;

/// <summary>
/// Splits lines into Discord message bodies of at most <c>maxLength</c> characters (UTF-16 code units).
/// A section heading (a line that starts with <c>**</c>) stays with the line that follows it when both fit,
/// so the heading is not left alone at the end of a message.
/// </summary>
public static class DiscordMessagePacker
{
    public const string TruncationNote = "… (truncated)";

    public static List<string> Pack(IReadOnlyList<string> lines, int maxLength)
    {
        if (maxLength < 1)
            throw new ArgumentOutOfRangeException(nameof(maxLength), "Message length limit must be positive.");

        var prepared = new List<string>(lines.Count);
        foreach (var line in lines)
            prepared.Add(Truncate(OneLine(line), maxLength));

        var messages = new List<string>();
        var current = new List<string>();
        var length = 0;

        void Flush()
        {
            if (current.Count == 0) return;
            messages.Add(string.Join('\n', current));
            current.Clear();
            length = 0;
        }

        for (var i = 0; i < prepared.Count; i++)
        {
            var line = prepared[i];
            if (line.Length == 0)
            {
                if (length == 0)
                    continue;

                if (!BlankFits(prepared, i, length, maxLength))
                {
                    Flush();
                    continue;
                }

                current.Add("");
                length += 1;
                continue;
            }

            if (IsHeading(line) && HeadingShouldMove(prepared, i, length, maxLength))
                Flush();

            var added = length == 0 ? line.Length : line.Length + 1;
            if (length > 0 && length + added > maxLength)
                Flush();

            current.Add(line);
            length += length == 0 ? line.Length : line.Length + 1;
        }

        Flush();
        return messages;
    }

    public static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static bool IsHeading(string line) =>
        line.StartsWith("**", StringComparison.Ordinal);

    /// <summary>
    /// A blank line is kept only when the next block still fits after it. The block is the next line, or a
    /// heading plus its first body line when those fit in an empty message. Otherwise the blank is dropped
    /// and the block starts the next message, so a chunk does not end on a blank or a stranded heading.
    /// </summary>
    private static bool BlankFits(IReadOnlyList<string> lines, int blankIndex, int length, int maxLength)
    {
        if (!TryBlockEnd(lines, blankIndex + 1, maxLength, out var end))
            return false;

        var withBlank = JoinLength(lines, blankIndex, end);
        return length + 1 + withBlank <= maxLength;
    }

    private static bool HeadingShouldMove(IReadOnlyList<string> lines, int headingIndex, int length, int maxLength)
    {
        if (length == 0 || !TryBlockEnd(lines, headingIndex, maxLength, out var end) || end == headingIndex)
            return false;

        var group = JoinLength(lines, headingIndex, end);
        return group <= maxLength && length + 1 + group > maxLength;
    }

    /// <summary>
    /// End index (inclusive) of the block that should stay with the content at <paramref name="start"/>.
    /// A heading extends through its next body line when that group fits in <paramref name="maxLength"/>.
    /// </summary>
    private static bool TryBlockEnd(IReadOnlyList<string> lines, int start, int maxLength, out int end)
    {
        end = -1;
        var content = NextContentIndex(lines, start);
        if (content < 0)
            return false;

        end = content;
        if (!IsHeading(lines[content]))
            return true;

        var body = NextContentIndex(lines, content + 1);
        if (body < 0)
            return true;

        if (JoinLength(lines, content, body) <= maxLength)
            end = body;
        return true;
    }

    private static int NextContentIndex(IReadOnlyList<string> lines, int start)
    {
        for (var i = start; i < lines.Count; i++)
        {
            if (lines[i].Length > 0)
                return i;
        }

        return -1;
    }

    private static int JoinLength(IReadOnlyList<string> lines, int start, int endInclusive)
    {
        var length = lines[start].Length;
        for (var i = start + 1; i <= endInclusive; i++)
            length += 1 + lines[i].Length;
        return length;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        if (maxLength <= TruncationNote.Length)
            return Cut(text, maxLength);
        return Cut(text, maxLength - TruncationNote.Length) + TruncationNote;
    }

    /// <summary>Cuts on a UTF-16 boundary that is not the middle of a surrogate pair.</summary>
    private static string Cut(string text, int maxChars)
    {
        if (maxChars <= 0) return "";
        if (text.Length <= maxChars) return text;
        if (char.IsHighSurrogate(text[maxChars - 1]))
            maxChars--;
        return maxChars <= 0 ? "" : text[..maxChars];
    }
}
