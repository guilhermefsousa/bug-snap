namespace BugSnap.Services;

public enum ThrottleBlockReason
{
    FingerprintRecent,
    GlobalRateLimit,
    CircuitOpen
}

/// <summary>
/// Thread-safe throttle for auto-capture: fingerprint dedup, global rate limit, circuit breaker.
/// </summary>
public sealed class AutoCaptureThrottle
{
    private readonly int _fingerprintThrottleSeconds;
    private readonly int _globalRateLimitPerMinute;
    private readonly int _circuitBreakerThreshold;
    private readonly int _circuitBreakerCooldownSeconds;
    private readonly Func<DateTime> _clock;

    private readonly object _lock = new();

    // fingerprint → time of last allowed acquisition
    private readonly Dictionary<string, DateTime> _fingerprintCache = new();

    // timestamps of all allowed acquisitions in the last 60 seconds (rolling window)
    private readonly List<DateTime> _rollingWindow = new();

    // when circuit was opened (null = circuit is closed)
    private DateTime? _circuitOpenedAt;

    // Events for telemetry — fired OUTSIDE the lock (callers must handle on correct thread)
    public event Action<string>? OnFingerprintBlocked;
    public event Action<int>? OnGlobalRateLimitHit;
    public event Action<int>? OnCircuitBreakerOpened;
    public event Action? OnCircuitBreakerClosed;

    public AutoCaptureThrottle(
        BugSnapOptions options,
        Func<DateTime>? clock = null)
    {
        _fingerprintThrottleSeconds = options.AutoCaptureFingerprintThrottleSeconds;
        _globalRateLimitPerMinute = options.AutoCaptureGlobalRateLimitPerMinute;
        _circuitBreakerThreshold = options.AutoCaptureCircuitBreakerThreshold;
        _circuitBreakerCooldownSeconds = options.AutoCaptureCircuitBreakerCooldownSeconds;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Returns the current count of acquisitions in the rolling 60-second window.
    /// Thread-safe read (takes lock briefly).
    /// </summary>
    public int GetCurrentWindowCount()
    {
        lock (_lock)
        {
            var now = _clock();
            PurgeExpiredEntries(now);
            return _rollingWindow.Count;
        }
    }

    /// <summary>
    /// Tries to acquire a slot. Returns false if blocked, and sets <paramref name="blocked"/> to the reason.
    /// </summary>
    public bool TryAcquire(string fingerprint, out ThrottleBlockReason? blocked)
    {
        ThrottleBlockReason? reason = null;
        bool circuitJustClosed = false;
        bool circuitJustOpened = false;
        int windowCountOnOpen = 0;
        bool fingerprintJustBlocked = false;
        bool globalJustBlocked = false;
        int windowCountOnGlobal = 0;

        lock (_lock)
        {
            var now = _clock();
            PurgeExpiredEntries(now);

            // --- Circuit breaker check ---
            if (_circuitOpenedAt.HasValue)
            {
                var elapsed = (now - _circuitOpenedAt.Value).TotalSeconds;
                if (elapsed >= _circuitBreakerCooldownSeconds)
                {
                    // Close the circuit
                    _circuitOpenedAt = null;
                    circuitJustClosed = true;
                }
                else
                {
                    reason = ThrottleBlockReason.CircuitOpen;
                    blocked = reason;
                    return false;
                }
            }

            // --- Fingerprint throttle check ---
            if (_fingerprintCache.TryGetValue(fingerprint, out var lastSeen))
            {
                var elapsed = (now - lastSeen).TotalSeconds;
                if (elapsed < _fingerprintThrottleSeconds)
                {
                    reason = ThrottleBlockReason.FingerprintRecent;
                    fingerprintJustBlocked = true;
                    blocked = reason;
                }
            }

            if (reason is null)
            {
                // --- Global rate limit check ---
                var countInWindow = _rollingWindow.Count;
                if (countInWindow >= _globalRateLimitPerMinute)
                {
                    reason = ThrottleBlockReason.GlobalRateLimit;
                    globalJustBlocked = true;
                    windowCountOnGlobal = countInWindow;
                    blocked = reason;
                }
            }

            if (reason is null)
            {
                // Acquire: record the fingerprint and add to rolling window
                _fingerprintCache[fingerprint] = now;
                _rollingWindow.Add(now);

                // Check if this tips the circuit breaker
                if (_rollingWindow.Count >= _circuitBreakerThreshold)
                {
                    _circuitOpenedAt = now;
                    circuitJustOpened = true;
                    windowCountOnOpen = _rollingWindow.Count;
                }
            }
        }

        // Fire events outside the lock to avoid deadlocks
        if (circuitJustClosed)
            OnCircuitBreakerClosed?.Invoke();

        if (fingerprintJustBlocked)
            OnFingerprintBlocked?.Invoke(fingerprint);

        if (globalJustBlocked)
            OnGlobalRateLimitHit?.Invoke(windowCountOnGlobal);

        if (circuitJustOpened)
            OnCircuitBreakerOpened?.Invoke(windowCountOnOpen);

        blocked = reason;
        return reason is null;
    }

    // Must be called inside lock
    private void PurgeExpiredEntries(DateTime now)
    {
        // Purge rolling window (60-second window)
        _rollingWindow.RemoveAll(t => (now - t).TotalSeconds >= 60);

        // Purge fingerprint cache
        var cutoff = now.AddSeconds(-_fingerprintThrottleSeconds);
        var toRemove = _fingerprintCache
            .Where(kv => kv.Value < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove)
            _fingerprintCache.Remove(key);
    }
}
