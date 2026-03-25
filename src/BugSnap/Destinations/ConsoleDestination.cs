using BugSnap.Models;
using Microsoft.JSInterop;

namespace BugSnap.Destinations;

/// <summary>
/// Writes bug report summary to browser console. For dev/testing only.
/// IJSRuntime is resolved lazily at submit time via the provided accessor,
/// since destinations are registered before the DI container is built.
/// </summary>
public class ConsoleDestination : IBugReportDestination
{
    private readonly Func<IJSRuntime> _jsRuntimeAccessor;

    /// <summary>
    /// Creates a ConsoleDestination that resolves IJSRuntime lazily.
    /// Usage in AddBugSnap: the library wires this automatically.
    /// </summary>
    internal ConsoleDestination(Func<IJSRuntime> jsRuntimeAccessor)
    {
        _jsRuntimeAccessor = jsRuntimeAccessor;
    }

    public string Name => "Console";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        try
        {
            var js = _jsRuntimeAccessor();
            var message = $"[BugSnap] Bug Report: {report.Title} | Severity: {report.Severity} | Route: {report.Context.CurrentRoute} | Requests: {report.Context.RecentRequests.Count} | JS Errors: {report.Context.RecentJsErrors.Count}";
            await js.InvokeVoidAsync("console.log", ct, message);
            return new BugReportResult(true, Name);
        }
        catch (Exception ex)
        {
            return new BugReportResult(false, Name, Error: ex.Message);
        }
    }
}
