namespace PcUptimeTelegramTracker.Worker.Models;

// Represents one completed PC session: from boot to the next shutdown/crash.
public class SessionSummary
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan AwakeDuration { get; set; }
    public TimeSpan SleepDuration { get; set; }
    public bool EndedCleanly { get; set; }

    public TimeSpan TotalDuration => EndTime - StartTime;
}