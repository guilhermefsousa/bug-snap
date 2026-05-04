using System.Diagnostics;
using System.Text.RegularExpressions;
using BugSnap.Models;
using Microsoft.Extensions.Options;

namespace BugSnap.Services;

public sealed class SeverityDetector
{
    private const int RetryThreshold = 3;

    private readonly List<Regex> _criticalRegexes;

    public SeverityDetector(IOptions<BugSnapOptions> options)
    {
        _criticalRegexes = [];
        foreach (var pattern in options.Value.CriticalUrlPatterns)
        {
            try
            {
                _criticalRegexes.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BugSnap] Invalid CriticalUrlPattern '{pattern}': {ex.Message}");
            }
        }
    }

    public BugSnapSeverity Detect(BugContextSnapshot context)
    {
        var requests = context.RecentRequests;
        var jsErrors = context.RecentJsErrors;

        // Rule 1: Critical URL with retry OR critical URL with 4xx/5xx → Critical
        if (requests.Any(r => MatchesCriticalUrl(r.Url) && r.StatusCode >= 400)
            || (IsRetryPattern(requests) && requests.Any(r => MatchesCriticalUrl(r.Url))))
            return BugSnapSeverity.Critical;

        // Rule 2: 5xx errors or JS errors → High
        if (requests.Any(r => r.StatusCode >= 500) || jsErrors.Count > 0)
            return BugSnapSeverity.High;

        // Rule 3: Retry pattern (≥3 same Method+Url) → High
        if (IsRetryPattern(requests))
            return BugSnapSeverity.High;

        // Rule 4: 4xx errors or SignalR not connected → Medium
        var signalRIssue = context.SignalRState is not null
            && !string.Equals(context.SignalRState, "Connected", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.SignalRState, "Connecting", StringComparison.OrdinalIgnoreCase);
        if (requests.Any(r => r.StatusCode >= 400 && r.StatusCode < 500) || signalRIssue)
            return BugSnapSeverity.Medium;

        return BugSnapSeverity.Low;
    }

    private static bool IsRetryPattern(IReadOnlyList<HttpActivityEntry> requests)
    {
        return requests
            .GroupBy(r => (
                Method: (r.Method ?? "").ToUpperInvariant(),
                Url: (r.Url ?? "").ToLowerInvariant()))
            .Any(g => g.Count() >= RetryThreshold);
    }

    private bool MatchesCriticalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        foreach (var regex in _criticalRegexes)
        {
            if (regex.IsMatch(url))
                return true;
        }

        return false;
    }
}
