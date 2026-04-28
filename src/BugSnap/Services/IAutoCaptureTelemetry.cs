namespace BugSnap.Services;

public interface IAutoCaptureTelemetry
{
    Task OnFingerprintBlockedAsync(string fingerprint, CancellationToken ct);
    Task OnGlobalRateLimitHitAsync(int countInWindow, CancellationToken ct);
    Task OnCircuitBreakerOpenedAsync(int countInWindow, CancellationToken ct);
    Task OnCircuitBreakerClosedAsync(CancellationToken ct);
}
