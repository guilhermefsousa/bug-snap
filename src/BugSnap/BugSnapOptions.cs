using BugSnap.Destinations;

namespace BugSnap;

public sealed class BugSnapOptions
{
    public string? AppName { get; set; }
    public string? AppVersion { get; set; }
    public string? Environment { get; set; }

    public int MaxHttpEntries { get; set; } = 20;
    public int MaxJsErrors { get; set; } = 10;
    public int MaxErrorSnippetLength { get; set; } = 500;
    public int RateLimitSeconds { get; set; } = 30;

    public List<IBugReportDestination> Destinations { get; set; } = [];

    /// <summary>
    /// When true, adds a ConsoleDestination that logs to browser console.
    /// IJSRuntime is resolved from DI automatically. For dev/testing only.
    /// </summary>
    public bool EnableConsoleDestination { get; set; }
}
