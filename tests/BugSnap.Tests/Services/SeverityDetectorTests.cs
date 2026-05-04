using BugSnap.Models;
using BugSnap.Services;
using Microsoft.Extensions.Options;

namespace BugSnap.Tests.Services;

public class SeverityDetectorTests
{
    private static SeverityDetector CreateDetector(params string[] criticalPatterns)
    {
        var options = Options.Create(new BugSnapOptions { CriticalUrlPatterns = criticalPatterns.ToList() });
        return new SeverityDetector(options);
    }

    // --- High: 5xx ---

    [Fact]
    public void Detect_When500Present_ShouldReturnHigh()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { Method = "GET", Url = "/api/data", StatusCode = 500 }],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.High, result);
    }

    // --- High: JS errors ---

    [Fact]
    public void Detect_WhenJsErrorPresent_ShouldReturnHigh()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests = [],
            RecentJsErrors = [new JsErrorEntry { Message = "TypeError: Cannot read property of null" }]
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.High, result);
    }

    // --- Medium: 4xx ---

    [Fact]
    public void Detect_When4xxPresent_ShouldReturnMedium()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { Method = "GET", Url = "/api/resource", StatusCode = 404 }],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Medium, result);
    }

    // --- Medium: SignalR disconnected ---

    [Fact]
    public void Detect_WhenSignalRDisconnected_ShouldReturnMedium()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            SignalRState = "Disconnected",
            RecentRequests = [],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Medium, result);
    }

    // --- Low: no issues ---

    [Fact]
    public void Detect_WhenAllOk_ShouldReturnLow()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            SignalRState = "Connected",
            RecentRequests = [new HttpActivityEntry { Method = "GET", Url = "/api/health", StatusCode = 200 }],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Low, result);
    }

    // --- High: retry pattern (reproduces issue #32 without critical URL configured) ---

    [Fact]
    public void Detect_WhenSamePostRepeated3TimesAllSuccess_ShouldReturnHigh()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/a1b2c3d4-e5f6-7890-abcd-ef1234567890/start", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/a1b2c3d4-e5f6-7890-abcd-ef1234567890/start", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/a1b2c3d4-e5f6-7890-abcd-ef1234567890/start", StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.High, result);
    }

    // --- Edge: 2 retries = below threshold → Low ---

    [Fact]
    public void Detect_WhenSamePostRepeated2Times_ShouldReturnLow()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/items", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/items", StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Low, result);
    }

    // --- Edge: same method but different URLs → no retry → Low ---

    [Fact]
    public void Detect_WhenSamePostRepeatedButDifferentUrls_ShouldReturnLow()
    {
        var detector = CreateDetector();
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/items/1", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/items/2", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/items/3", StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Low, result);
    }

    // --- Critical: critical URL + retry (reproduces issue #32 with pattern configured) ---

    [Fact]
    public void Detect_WhenCriticalUrlMatchesAndRetryHappens_ShouldReturnCritical()
    {
        var detector = CreateDetector(@"/api/connections/[^/]+/start");
        var connectionUrl = "/api/connections/a1b2c3d4-e5f6-7890-abcd-ef1234567890/start";
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = connectionUrl, StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = connectionUrl, StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = connectionUrl, StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = connectionUrl, StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = connectionUrl, StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Critical, result);
    }

    // --- Critical: critical URL + 4xx status ---

    [Fact]
    public void Detect_WhenCriticalUrlMatchesAndStatus4xx_ShouldReturnCritical()
    {
        var detector = CreateDetector(@"/api/auth");
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/auth/token", StatusCode = 401 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Critical, result);
    }

    // --- No escalation: single success request on critical URL → Low ---

    [Fact]
    public void Detect_WhenCriticalUrlMatchesButNoRetryAndNoError_ShouldNotEscalateToCritical()
    {
        var detector = CreateDetector(@"/api/auth");
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/auth/token", StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.Low, result);
    }

    // --- Resilience: invalid regex pattern must not throw ---

    [Fact]
    public void Detect_WithInvalidRegexPattern_ShouldNotThrow()
    {
        var detector = CreateDetector(@"[invalid-regex(");
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { Method = "GET", Url = "/api/data", StatusCode = 200 }],
            RecentJsErrors = []
        };

        var exception = Record.Exception(() => detector.Detect(context));

        Assert.Null(exception);
    }

    // --- Fallback: no critical patterns → generic rules apply ---

    [Fact]
    public void Detect_WhenCriticalUrlPatternsEmpty_ShouldFallBackToGenericRules()
    {
        var detector = CreateDetector(); // no critical patterns
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/abc/start", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/abc/start", StatusCode = 200 },
                new HttpActivityEntry { Method = "POST", Url = "/api/connections/abc/start", StatusCode = 200 }
            ],
            RecentJsErrors = []
        };

        // Without critical patterns, 3 retries of status 200 → High (generic retry rule)
        var result = detector.Detect(context);

        Assert.Equal(BugSnapSeverity.High, result);
    }
}
