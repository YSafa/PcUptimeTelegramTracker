using System.ComponentModel;
using System.Diagnostics;
using PcUptimeTelegramTracker.Worker.Storage;

namespace PcUptimeTelegramTracker.Worker.Services;

// Periodically samples running processes and accumulates CPU time per
// process name (not PID, since the same app can restart with a new PID).
// Sampling every 60s keeps overhead negligible while still catching
// which apps dominate usage over a session/day.
public class ProcessUsageCollector
{
    private readonly ILogger<ProcessUsageCollector> _logger;
    private readonly UsageRepository _repository;
    
    // Tracks the last-seen TotalProcessorTime per PID, so we can compute
    // deltas between samples instead of double-counting cumulative totals.
    private readonly Dictionary<int, TimeSpan> _lastSeenProcessorTime = new();

    // Exclude our own process — its usage isn't meaningful for "which app is heavy".
    private static readonly string SelfProcessName =
        Process.GetCurrentProcess().ProcessName;

    public ProcessUsageCollector(ILogger<ProcessUsageCollector> logger, UsageRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    public void SampleOnce(DateTime sessionStartTime)
    {
        var processes = Process.GetProcesses();

        foreach (var process in processes)
        {
            try
            {
                if (process.ProcessName == SelfProcessName ||
                    process.ProcessName is "Idle" or "System")
                {
                    continue;
                }

                var currentTotal = process.TotalProcessorTime;

                if (_lastSeenProcessorTime.TryGetValue(process.Id, out var previousTotal))
                {
                    var delta = currentTotal - previousTotal;
                    // A negative delta means this PID was reused by a different
                    // process since our last sample — skip it this round rather
                    // than corrupting the accumulated total.
                    if (delta > TimeSpan.Zero)
                    {
                        _repository.AddAppUsage(sessionStartTime, process.ProcessName, delta);
                    }
                }

                _lastSeenProcessorTime[process.Id] = currentTotal;
            }
            catch (Win32Exception)
            {
                // Access denied for this process — skip silently.
            }
            catch (InvalidOperationException)
            {
                // Process exited between GetProcesses() and reading its properties.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}