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

    /// <summary>
    /// When true, registers auto-capture services that silently report Blazor and JS errors.
    /// </summary>
    public bool EnableAutoCapture { get; set; } = false;

    /// <summary>
    /// Minimum seconds between submissions of the same fingerprint.
    /// </summary>
    public int AutoCaptureFingerprintThrottleSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum number of auto-capture submissions allowed per minute globally.
    /// </summary>
    public int AutoCaptureGlobalRateLimitPerMinute { get; set; } = 5;

    /// <summary>
    /// Number of submissions per minute that trips the circuit breaker.
    /// </summary>
    public int AutoCaptureCircuitBreakerThreshold { get; set; } = 20;

    /// <summary>
    /// Seconds the circuit breaker stays open after being tripped.
    /// </summary>
    public int AutoCaptureCircuitBreakerCooldownSeconds { get; set; } = 300;
}
