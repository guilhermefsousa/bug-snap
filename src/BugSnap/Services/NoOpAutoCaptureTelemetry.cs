namespace BugSnap.Services;

public sealed class NoOpAutoCaptureTelemetry : IAutoCaptureTelemetry
{
    public Task OnFingerprintBlockedAsync(string fingerprint, CancellationToken ct) => Task.CompletedTask;
    public Task OnGlobalRateLimitHitAsync(int countInWindow, CancellationToken ct) => Task.CompletedTask;
    public Task OnCircuitBreakerOpenedAsync(int countInWindow, CancellationToken ct) => Task.CompletedTask;
    public Task OnCircuitBreakerClosedAsync(CancellationToken ct) => Task.CompletedTask;
}
