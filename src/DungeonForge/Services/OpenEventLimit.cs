namespace DungeonForge.Services;

/// <summary>How many events may be open (not completed and not cancelled) at once.</summary>
public static class OpenEventLimit
{
    public static string Counter(int open, int max) => $"{open}/{max} open events";

    public static string? Rejection(int open, int max) =>
        open >= max
            ? $"There are already {Counter(open, max)}. Complete one with /dfcompleteevent or cancel one with /dfcancelevent, then try again."
            : null;
}
