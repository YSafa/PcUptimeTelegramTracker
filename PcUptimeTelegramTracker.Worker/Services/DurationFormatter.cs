namespace PcUptimeTelegramTracker.Worker.Services;

// Turns a TimeSpan into a short, human-readable Turkish string
// (e.g. "1 sa 24 dk 32 sn"), omitting zero-value units.
public static class DurationFormatter
{
    public static string Format(TimeSpan duration)
    {
        var totalHours = (int)duration.TotalHours;
        var parts = new List<string>();

        if (totalHours > 0) parts.Add($"{totalHours} sa");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes} dk");
        if (duration.Seconds > 0 || parts.Count == 0) parts.Add($"{duration.Seconds} sn");

        return string.Join(" ", parts);
    }
}