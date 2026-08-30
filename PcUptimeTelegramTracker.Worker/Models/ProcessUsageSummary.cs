namespace PcUptimeTelegramTracker.Worker.Models;

// One entry in the "top resource-consuming apps" list for a session/day.
public class ProcessUsageSummary
{
    public string ProcessName { get; set; } = string.Empty;
    public TimeSpan TotalCpuTime { get; set; }
}