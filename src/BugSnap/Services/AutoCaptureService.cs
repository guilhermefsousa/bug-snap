using BugSnap.Models;

namespace BugSnap.Services;

/// <summary>
/// Silently reports captured errors. Never throws — all exceptions are swallowed to avoid loops.
/// </summary>
public sealed class AutoCaptureService(
    BugSnapOptions options,
    BugContextCollector contextCollector,
    MultiDestinationDispatcher dispatcher,
    AutoCaptureThrottle throttle,
    IAutoCaptureTelemetry telemetry)
{
    public async Task ReportAsync(
        Exception? blazorEx,
        JsErrorEntry? jsError,
        string source,
        CancellationToken ct)
    {
        // Kill switch: skip dispatch entirely when disabled.
        // Honors BugSnap__AutoCaptureDisabled env var via /api/client-config bootstrap.
        if (!options.EnableAutoCapture) return;

        try
        {
            // 1. Collect context
            var context = await contextCollector.CollectAsync(ct);

            // 2. Prepend Blazor exception as a JsErrorEntry at the top of RecentJsErrors
            if (blazorEx is not null)
            {
                var blazorEntry = new JsErrorEntry
                {
                    Source = "blazor",
                    Message = blazorEx.Message,
                    StackTrace = blazorEx.StackTrace,
                    TimestampUtc = DateTime.UtcNow
                };

                var list = new List<JsErrorEntry> { blazorEntry };
                list.AddRange(context.RecentJsErrors);
                context.RecentJsErrors = list;
            }

            // 3. Compute fingerprint
            var fingerprint = FingerprintGenerator.Generate(context);

            // 4. Throttle check
            if (!throttle.TryAcquire(fingerprint, out var reason))
            {
                var windowCount = throttle.GetCurrentWindowCount();
                switch (reason)
                {
                    case ThrottleBlockReason.FingerprintRecent:
                        await telemetry.OnFingerprintBlockedAsync(fingerprint, ct);
                        break;
                    case ThrottleBlockReason.GlobalRateLimit:
                        await telemetry.OnGlobalRateLimitHitAsync(windowCount, ct);
                        break;
                    case ThrottleBlockReason.CircuitOpen:
                        await telemetry.OnCircuitBreakerOpenedAsync(windowCount, ct);
                        break;
                }
                return;
            }

            // 5. Build report
            var title = "[auto] " + (blazorEx?.Message ?? jsError?.Message ?? "Unknown error");
            if (title.Length > 255) title = title[..255];

            var report = new BugReport
            {
                AutoDetected = true,
                Severity = SeverityDetector.Detect(context),
                Category = context.SuggestedCategory ?? BugSnapCategory.Other,
                Fingerprint = fingerprint,
                Title = title,
                Description = source,
                Context = context
            };

            // 6. Dispatch
            await dispatcher.DispatchAsync(report, ct);
        }
        catch
        {
            // Auto-capture must never propagate exceptions — swallow silently.
        }
    }
}
