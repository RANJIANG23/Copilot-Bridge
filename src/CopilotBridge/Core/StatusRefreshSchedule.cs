namespace CopilotBridge.Core;

internal static class StatusRefreshSchedule
{
    internal static TimeSpan? NextInterval(
        bool overviewIsVisible,
        bool windowIsActive,
        bool windowIsMinimized,
        int consecutiveFailures,
        bool paused = false)
    {
        if (paused) return null;

        if (consecutiveFailures > 0)
        {
            var seconds = Math.Min(120, 30 * (1 << Math.Min(consecutiveFailures - 1, 2)));
            return TimeSpan.FromSeconds(seconds);
        }

        return overviewIsVisible && windowIsActive && !windowIsMinimized
            ? TimeSpan.FromSeconds(10)
            : TimeSpan.FromSeconds(60);
    }
}
