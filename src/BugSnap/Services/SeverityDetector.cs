using BugSnap.Models;

namespace BugSnap.Services;

public static class SeverityDetector
{
    public static BugSnapSeverity Detect(BugContextSnapshot context)
    {
        var requests = context.RecentRequests;
        var jsErrors = context.RecentJsErrors;

        // 5xx errors or JS errors → High
        if (requests.Any(r => r.StatusCode >= 500) || jsErrors.Count > 0)
            return BugSnapSeverity.High;

        // 4xx errors or SignalR issues → Medium
        var signalRIssue = context.SignalRState is not null
            && !string.Equals(context.SignalRState, "Connected", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.SignalRState, "Connecting", StringComparison.OrdinalIgnoreCase);
        if (requests.Any(r => r.StatusCode >= 400 && r.StatusCode < 500) || signalRIssue)
            return BugSnapSeverity.Medium;

        return BugSnapSeverity.Low;
    }
}
