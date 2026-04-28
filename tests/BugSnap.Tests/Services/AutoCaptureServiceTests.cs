using BugSnap;
using BugSnap.Destinations;
using BugSnap.Models;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

/// <summary>
/// Tests for AutoCaptureService using manually-constructed fakes.
/// </summary>
public class AutoCaptureServiceTests
{
    // --- Fakes ---

    private sealed class FakeDestination : IBugReportDestination
    {
        public string Name => "Fake";
        public List<BugReport> ReceivedReports { get; } = [];
        public bool ShouldThrow { get; set; }

        public Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
        {
            if (ShouldThrow) throw new Exception("destination exploded");
            ReceivedReports.Add(report);
            return Task.FromResult(new BugReportResult(true, Name));
        }
    }

    private sealed class FakeTelemetry : IAutoCaptureTelemetry
    {
        public List<string> FingerprintBlockedCalls { get; } = [];
        public List<int> GlobalRateLimitCalls { get; } = [];
        public List<int> CircuitBreakerOpenedCalls { get; } = [];
        public int CircuitBreakerClosedCount { get; private set; }

        public Task OnFingerprintBlockedAsync(string fingerprint, CancellationToken ct)
        { FingerprintBlockedCalls.Add(fingerprint); return Task.CompletedTask; }

        public Task OnGlobalRateLimitHitAsync(int countInWindow, CancellationToken ct)
        { GlobalRateLimitCalls.Add(countInWindow); return Task.CompletedTask; }

        public Task OnCircuitBreakerOpenedAsync(int countInWindow, CancellationToken ct)
        { CircuitBreakerOpenedCalls.Add(countInWindow); return Task.CompletedTask; }

        public Task OnCircuitBreakerClosedAsync(CancellationToken ct)
        { CircuitBreakerClosedCount++; return Task.CompletedTask; }
    }

    // Build a minimal AutoCaptureService wired with real lightweight collaborators.
    // BugContextCollector / MultiDestinationDispatcher need to be constructed,
    // so we use a helper that returns both the service and the fake destination.
    private static (AutoCaptureService service, FakeDestination destination, FakeTelemetry telemetry)
        Build(BugSnapOptions? options = null, Func<DateTime>? clock = null)
    {
        options ??= new BugSnapOptions
        {
            EnableAutoCapture = true,
            AutoCaptureFingerprintThrottleSeconds = 300,
            AutoCaptureGlobalRateLimitPerMinute = 100,
            AutoCaptureCircuitBreakerThreshold = 1000,
            AutoCaptureCircuitBreakerCooldownSeconds = 300
        };

        var dest = new FakeDestination();
        var dispatcher = new MultiDestinationDispatcher([dest], options);
        var throttle = new AutoCaptureThrottle(options, clock);
        var telemetry = new FakeTelemetry();

        var service = new AutoCaptureService(
            options,
            new StubContextCollector(),
            dispatcher,
            throttle,
            telemetry);

        return (service, dest, telemetry);
    }

    // StubContextCollector: returns an empty snapshot without any JS/HTTP
    private sealed class StubContextCollector : BugContextCollector
    {
        public StubContextCollector() : base(null!, null!, null!, null!, null!)
        { }

        // Override CollectAsync to avoid real DI dependencies
        public override Task<BugContextSnapshot> CollectAsync(CancellationToken ct = default)
            => Task.FromResult(new BugContextSnapshot
            {
                CurrentRoute = "/test",
                AppVersion = "1.0.0",
                RecentJsErrors = [],
                RecentRequests = []
            });
    }

    // --- Simplified builder that injects real AlwaysBlockThrottle ---
    // Since TryAcquire is not virtual, we use an already-exhausted throttle.
    private static (AutoCaptureService service, FakeDestination destination, FakeTelemetry telemetry)
        BuildBlocking()
    {
        var options = new BugSnapOptions
        {
            EnableAutoCapture = true,
            AutoCaptureFingerprintThrottleSeconds = 9999,
            AutoCaptureGlobalRateLimitPerMinute = 1, // allow only 1 total
            AutoCaptureCircuitBreakerThreshold = 1000
        };

        var dest = new FakeDestination();
        var dispatcher = new MultiDestinationDispatcher([dest], options);
        var throttle = new AutoCaptureThrottle(options);
        throttle.TryAcquire("warmup-fp", out _); // exhaust the single global slot

        var telemetry = new FakeTelemetry();
        var service = new AutoCaptureService(options, new StubContextCollector(), dispatcher, throttle, telemetry);

        return (service, dest, telemetry);
    }

    // 1. When throttle allows → dispatch called with AutoDetected=true
    [Fact]
    public async Task ReportAsync_WhenThrottleAllows_ShouldDispatchWithAutoDetectedTrue()
    {
        // Arrange
        var (svc, dest, _) = Build();

        // Act
        await svc.ReportAsync(null, new JsErrorEntry { Message = "test" }, "js", default);

        // Assert
        Assert.Single(dest.ReceivedReports);
        Assert.True(dest.ReceivedReports[0].AutoDetected);
    }

    // 2. When throttle blocks → no dispatch, telemetry called
    [Fact]
    public async Task ReportAsync_WhenThrottleBlocks_ShouldNotDispatch_ShouldCallTelemetry()
    {
        // Arrange
        var (svc, dest, telemetry) = BuildBlocking();

        // Act
        await svc.ReportAsync(null, new JsErrorEntry { Message = "blocked" }, "js", default);

        // Assert
        Assert.Empty(dest.ReceivedReports);
        Assert.True(
            telemetry.GlobalRateLimitCalls.Count > 0 || telemetry.FingerprintBlockedCalls.Count > 0,
            "Expected at least one telemetry event to be fired");
    }

    // 3. When dispatcher throws → exception is swallowed, does not propagate
    [Fact]
    public async Task ReportAsync_WhenDispatcherThrows_ShouldNotPropagate()
    {
        // Arrange
        var options = new BugSnapOptions
        {
            EnableAutoCapture = true,
            AutoCaptureFingerprintThrottleSeconds = 300,
            AutoCaptureGlobalRateLimitPerMinute = 100,
            AutoCaptureCircuitBreakerThreshold = 1000
        };
        var dest = new FakeDestination { ShouldThrow = true };
        var dispatcher = new MultiDestinationDispatcher([dest], options);
        var throttle = new AutoCaptureThrottle(options);
        var telemetry = new FakeTelemetry();
        var svc = new AutoCaptureService(options, new StubContextCollector(), dispatcher, throttle, telemetry);

        // Act — must NOT throw
        var exception = await Record.ExceptionAsync(
            () => svc.ReportAsync(null, new JsErrorEntry { Message = "boom" }, "js", default));

        // Assert
        Assert.Null(exception);
    }

    // 4. With Blazor exception → fingerprint computed from context, stacktrace included
    [Fact]
    public async Task ReportAsync_WithBlazorException_ShouldGenerateFingerprintFromContext_AndIncludeStackTrace()
    {
        // Arrange
        var (svc, dest, _) = Build();
        Exception ex;
        try { throw new InvalidOperationException("blazor crash"); }
        catch (Exception caught) { ex = caught; }

        // Act
        await svc.ReportAsync(ex, null, "blazor", default);

        // Assert
        Assert.Single(dest.ReceivedReports);
        var report = dest.ReceivedReports[0];
        Assert.NotNull(report.Fingerprint);
        Assert.Contains("[auto]", report.Title);
        Assert.Contains("blazor crash", report.Title);
        Assert.True(report.AutoDetected);

        // Blazor exception should be prepended to RecentJsErrors with StackTrace
        var firstJsError = report.Context.RecentJsErrors.FirstOrDefault();
        Assert.NotNull(firstJsError);
        Assert.Equal("blazor crash", firstJsError.Message);
        Assert.NotNull(firstJsError.StackTrace); // StackTrace is present since we threw it
    }

    // 5. With JsError → dispatch with source=js
    [Fact]
    public async Task ReportAsync_WithJsError_ShouldDispatchWithSourceJs()
    {
        // Arrange
        var (svc, dest, _) = Build();
        var jsError = new JsErrorEntry { Message = "undefined is not a function", Source = "app.js" };

        // Act
        await svc.ReportAsync(null, jsError, "js", default);

        // Assert
        Assert.Single(dest.ReceivedReports);
        var report = dest.ReceivedReports[0];
        Assert.Equal("js", report.Description); // Description = source
        Assert.True(report.AutoDetected);
    }

    // 6. Kill switch — when EnableAutoCapture=false, no dispatch and no telemetry
    [Fact]
    public async Task ReportAsync_WhenAutoCaptureDisabled_ShouldNotDispatch()
    {
        // Arrange — explicitly disable auto-capture
        var options = new BugSnapOptions
        {
            EnableAutoCapture = false,
            AutoCaptureFingerprintThrottleSeconds = 300,
            AutoCaptureGlobalRateLimitPerMinute = 100,
            AutoCaptureCircuitBreakerThreshold = 1000
        };
        var (svc, dest, telemetry) = Build(options);

        // Act
        await svc.ReportAsync(null, new JsErrorEntry { Message = "should-be-skipped" }, "js", default);

        // Assert — no dispatch, no telemetry side effects
        Assert.Empty(dest.ReceivedReports);
        Assert.Empty(telemetry.FingerprintBlockedCalls);
        Assert.Empty(telemetry.GlobalRateLimitCalls);
        Assert.Empty(telemetry.CircuitBreakerOpenedCalls);
    }
}
