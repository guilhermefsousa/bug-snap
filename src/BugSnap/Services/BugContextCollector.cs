using BugSnap.Models;
using Microsoft.AspNetCore.Components;

namespace BugSnap.Services;

public class BugContextCollector(
    NavigationManager navigation,
    JsErrorCollector jsErrorCollector,
    HttpActivityBuffer httpBuffer,
    IBugContextProvider contextProvider,
    BugSnapOptions options)
{
    public virtual async Task<BugContextSnapshot> CollectAsync(CancellationToken ct = default)
    {
        var uri = new Uri(navigation.Uri);
        var currentRoute = uri.PathAndQuery;

        var browserInfo = await jsErrorCollector.GetBrowserInfoAsync();
        var screenSize = await jsErrorCollector.GetScreenSizeAsync();
        var jsErrors = await jsErrorCollector.GetErrorsAsync();
        var consoleErrors = await jsErrorCollector.GetConsoleErrorsAsync();
        var breadcrumbs = await jsErrorCollector.GetBreadcrumbsAsync();
        var customContext = await contextProvider.GetCustomContextAsync(ct);

        // performance.memory is Chromium-only (null on Firefox/Safari); GC.GetTotalMemory
        // is the cross-browser fallback that always works in the WASM runtime.
        var memory = await jsErrorCollector.GetMemoryInfoAsync();
        memory.ManagedHeapBytes = GC.GetTotalMemory(false);

        var recentRequests = httpBuffer.GetRecentActivity();

        // Most recent TraceId and CorrelationId from HTTP entries
        string? traceId = null;
        string? correlationId = null;
        for (int i = recentRequests.Count - 1; i >= 0; i--)
        {
            var entry = recentRequests[i];
            if (traceId is null && entry.TraceId is not null)
                traceId = entry.TraceId;
            if (correlationId is null && entry.CorrelationId is not null)
                correlationId = entry.CorrelationId;
            if (traceId is not null && correlationId is not null)
                break;
        }

        var snapshot = new BugContextSnapshot
        {
            CurrentRoute = currentRoute,
            BrowserInfo = browserInfo,
            ScreenSize = screenSize,
            AppName = options.AppName,
            AppVersion = options.AppVersion,
            Environment = options.Environment,
            RecentRequests = recentRequests,
            RecentJsErrors = jsErrors,
            Memory = memory,
            RecentConsoleErrors = consoleErrors,
            Breadcrumbs = breadcrumbs,
            CustomContext = customContext,
            PageInstanceId = Guid.NewGuid().ToString("N"),
            CollectedAtUtc = DateTime.UtcNow,
            TraceId = traceId,
            CorrelationId = correlationId
        };

        // Auto-suggest category based on collected evidence
        snapshot.SuggestedCategory = CategorySuggester.Suggest(snapshot);

        return snapshot;
    }
}
