using BugSnap;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class AutoCaptureThrottleTests
{
    private static BugSnapOptions MakeOptions(
        int fingerprintThrottle = 300,
        int globalRate = 5,
        int cbThreshold = 20,
        int cbCooldown = 300)
        => new()
        {
            EnableAutoCapture = true,
            AutoCaptureFingerprintThrottleSeconds = fingerprintThrottle,
            AutoCaptureGlobalRateLimitPerMinute = globalRate,
            AutoCaptureCircuitBreakerThreshold = cbThreshold,
            AutoCaptureCircuitBreakerCooldownSeconds = cbCooldown
        };

    // 1. First fingerprint is always allowed
    [Fact]
    public void TryAcquire_WhenFirstFingerprint_ShouldAllow()
    {
        // Arrange
        var throttle = new AutoCaptureThrottle(MakeOptions());

        // Act
        var allowed = throttle.TryAcquire("fp-abc", out var reason);

        // Assert
        Assert.True(allowed);
        Assert.Null(reason);
    }

    // 2. Same fingerprint within throttle window is blocked + event fired
    [Fact]
    public void TryAcquire_WhenSameFingerprintWithinWindow_ShouldBlock_AndFireEvent()
    {
        // Arrange
        var throttle = new AutoCaptureThrottle(MakeOptions(fingerprintThrottle: 300));
        string? capturedFp = null;
        throttle.OnFingerprintBlocked += fp => capturedFp = fp;

        throttle.TryAcquire("fp-dup", out _); // first call allowed

        // Act
        var allowed = throttle.TryAcquire("fp-dup", out var reason);

        // Assert
        Assert.False(allowed);
        Assert.Equal(ThrottleBlockReason.FingerprintRecent, reason);
        Assert.Equal("fp-dup", capturedFp);
    }

    // 3. Same fingerprint after window has expired → allowed again
    [Fact]
    public void TryAcquire_WhenSameFingerprintAfterWindow_ShouldAllow()
    {
        // Arrange
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = () => now;
        var throttle = new AutoCaptureThrottle(MakeOptions(fingerprintThrottle: 60), clock);

        throttle.TryAcquire("fp-timed", out _); // acquire at T=0

        // Advance time past the fingerprint TTL
        now = now.AddSeconds(61);

        // Act
        var allowed = throttle.TryAcquire("fp-timed", out var reason);

        // Assert
        Assert.True(allowed);
        Assert.Null(reason);
    }

    // 4. Global rate limit exceeded → blocked + event fired
    [Fact]
    public void TryAcquire_WhenGlobalLimitExceeded_ShouldBlock_AndFireEvent()
    {
        // Arrange
        var throttle = new AutoCaptureThrottle(MakeOptions(globalRate: 3, cbThreshold: 100));
        int? capturedCount = null;
        throttle.OnGlobalRateLimitHit += count => capturedCount = count;

        // Use distinct fingerprints to avoid fingerprint throttle
        throttle.TryAcquire("fp-1", out _);
        throttle.TryAcquire("fp-2", out _);
        throttle.TryAcquire("fp-3", out _); // hits limit (3 acquired)

        // Act — 4th distinct fingerprint, but global limit = 3 already reached
        var allowed = throttle.TryAcquire("fp-4", out var reason);

        // Assert
        Assert.False(allowed);
        Assert.Equal(ThrottleBlockReason.GlobalRateLimit, reason);
        Assert.NotNull(capturedCount);
        Assert.True(capturedCount >= 3);
    }

    // 5. Circuit breaker threshold exceeded → opens + event fired
    [Fact]
    public void TryAcquire_WhenCircuitBreakerThresholdExceeded_ShouldOpen_AndFireEvent()
    {
        // Arrange
        const int threshold = 3;
        var throttle = new AutoCaptureThrottle(MakeOptions(globalRate: 100, cbThreshold: threshold));
        int? capturedCount = null;
        throttle.OnCircuitBreakerOpened += count => capturedCount = count;

        // Fill up to threshold
        throttle.TryAcquire("fp-a", out _);
        throttle.TryAcquire("fp-b", out _);
        throttle.TryAcquire("fp-c", out _); // this one trips the circuit

        // Act — circuit should now be open
        var allowed = throttle.TryAcquire("fp-d", out var reason);

        // Assert
        Assert.False(allowed);
        Assert.Equal(ThrottleBlockReason.CircuitOpen, reason);
        Assert.NotNull(capturedCount);
    }

    // 6. Circuit breaker closes after cooldown → event fired + next call allowed
    [Fact]
    public void TryAcquire_WhenCircuitBreakerCooldownPassed_ShouldClose_AndFireEvent_ThenAllow()
    {
        // Arrange
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = () => now;
        const int threshold = 2;
        const int cooldown = 300;
        var throttle = new AutoCaptureThrottle(
            MakeOptions(globalRate: 100, cbThreshold: threshold, cbCooldown: cooldown),
            clock);

        bool closedFired = false;
        throttle.OnCircuitBreakerClosed += () => closedFired = true;

        // Trip the circuit
        throttle.TryAcquire("fp-1", out _);
        throttle.TryAcquire("fp-2", out _); // trips circuit at T=0

        // Verify circuit is open
        var blockedResult = throttle.TryAcquire("fp-3", out var blockedReason);
        Assert.False(blockedResult);
        Assert.Equal(ThrottleBlockReason.CircuitOpen, blockedReason);

        // Advance time past cooldown
        now = now.AddSeconds(cooldown + 1);

        // Act — first call after cooldown should close circuit and allow
        var allowed = throttle.TryAcquire("fp-new", out var reason);

        // Assert
        Assert.True(allowed);
        Assert.Null(reason);
        Assert.True(closedFired);
    }

    // 7. While circuit is open, ALL fingerprints (even new ones) are blocked
    [Fact]
    public void TryAcquire_WhenCircuitOpen_ShouldBlockAllFingerprints()
    {
        // Arrange
        var throttle = new AutoCaptureThrottle(MakeOptions(globalRate: 100, cbThreshold: 2));

        // Trip the circuit
        throttle.TryAcquire("fp-x", out _);
        throttle.TryAcquire("fp-y", out _); // trips circuit

        // Act — try brand-new fingerprints
        var r1 = throttle.TryAcquire("fp-brand-new-1", out var reason1);
        var r2 = throttle.TryAcquire("fp-brand-new-2", out var reason2);

        // Assert
        Assert.False(r1);
        Assert.False(r2);
        Assert.Equal(ThrottleBlockReason.CircuitOpen, reason1);
        Assert.Equal(ThrottleBlockReason.CircuitOpen, reason2);
    }
}
