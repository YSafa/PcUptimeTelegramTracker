namespace PcUptimeTelegramTracker.Worker.Models;

// Tracks accumulated time-in-state for the current day.
public class DailyUsageSummary
{
    public TimeSpan TotalAwakeTime { get; set; } = TimeSpan.Zero;
    public TimeSpan TotalSleepTime { get; set; } = TimeSpan.Zero;

    // Time we couldn't classify — e.g. after an unexpected shutdown (Event ID 41),
    // where we don't know what state the machine was actually in.
    public TimeSpan TotalUnknownTime { get; set; } = TimeSpan.Zero;
}