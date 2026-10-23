using System.Globalization;

namespace DungeonForge.Services;

/// <summary>Parses the date/time arguments of /dfcreateevent (dd.MM.yyyy and HH:mm, invariant) in a given time zone.</summary>
public static class EventDateParser
{
    public const string DateFormat = "dd.MM.yyyy";
    public const string TimeFormat = "HH:mm";

    private static readonly string[] DateFormats = { "dd.MM.yyyy", "d.M.yyyy" };
    private static readonly string[] TimeFormats = { "HH:mm", "H:mm" };

    public static bool TryParse(string date, string time, TimeZoneInfo zone, DateTimeOffset now,
        out DateTimeOffset start, out string? error)
    {
        start = default;

        if (!DateOnly.TryParseExact(date?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            error = $"'{date}' is not a valid date. Use the format {DateFormat}, e.g. 24.10.2026.";
            return false;
        }

        if (!TimeOnly.TryParseExact(time?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            error = $"'{time}' is not a valid time. Use the format {TimeFormat} (24 hours), e.g. 18:00.";
            return false;
        }

        var local = d.ToDateTime(t, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            error = $"{local:dd.MM.yyyy HH:mm} does not exist in time zone {zone.Id} (daylight saving time gap). Pick another time.";
            return false;
        }

        start = new DateTimeOffset(local, zone.GetUtcOffset(local));
        if (start <= now)
        {
            error = $"{start:dd.MM.yyyy HH:mm} is in the past (it is now {TimeZoneInfo.ConvertTime(now, zone):dd.MM.yyyy HH:mm} {zone.Id}).";
            return false;
        }

        error = null;
        return true;
    }
}
