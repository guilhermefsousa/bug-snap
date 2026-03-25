using BugSnap.Models;

namespace BugSnap.Services;

public static class CategorySuggester
{
    private static readonly HashSet<string> _connectedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connected", "Connecting"
    };

    /// <summary>
    /// Suggests a bug category based on collected context.
    /// Rules (in priority order):
    /// 1. Has HTTP 401/403 → Auth
    /// 2. Has HTTP 5xx → API
    /// 3. SignalR disconnected/failed → SignalR
    /// 4. Has JS errors → UI
    /// 5. Has HTTP 4xx (excluding auth) → API
    /// 6. Has slow requests (>3s) → Performance
    /// 7. Default → Other
    /// </summary>
    public static BugSnapCategory Suggest(BugContextSnapshot context)
    {
        var requests = context.RecentRequests;
        var jsErrors = context.RecentJsErrors;

        // Auth errors — highest priority
        if (requests.Any(r => r.StatusCode is 401 or 403))
            return BugSnapCategory.Auth;

        // Server errors — clear API issue
        if (requests.Any(r => r.StatusCode >= 500))
            return BugSnapCategory.API;

        // SignalR disconnection (not "Connected" or "Connecting" — those are normal)
        if (context.SignalRState is not null && !_connectedStates.Contains(context.SignalRState))
            return BugSnapCategory.SignalR;

        // JS errors — likely UI issue
        if (jsErrors.Count > 0)
            return BugSnapCategory.UI;

        // Client errors 4xx (after JS errors — a 404 with no JS error is lower priority)
        if (requests.Any(r => r.StatusCode >= 400 && r.StatusCode < 500))
            return BugSnapCategory.API;

        // Slow requests
        if (requests.Any(r => r.DurationMs > 3000))
            return BugSnapCategory.Performance;

        return BugSnapCategory.Other;
    }
}
