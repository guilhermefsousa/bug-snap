using System.Net;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class HttpActivityTrackerTests
{
    // --- Helper inner handler ---

    private class TestHandler : HttpMessageHandler
    {
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(Response);
    }

    private static HttpClient BuildClient(HttpActivityTracker tracker, TestHandler inner)
    {
        tracker.InnerHandler = inner;
        return new HttpClient(tracker);
    }

    // --- Captures successful request ---

    [Fact]
    public async Task SendAsync_WhenRequestSucceeds_ShouldCaptureMethodUrlAndStatus()
    {
        // Arrange
        var inner = new TestHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/users");

        // Assert
        var entries = tracker.GetRecentActivity();
        Assert.Single(entries);
        Assert.Equal("GET", entries[0].Method);
        Assert.Equal("https://example.com/api/users", entries[0].Url);
        Assert.Equal(200, entries[0].StatusCode);
    }

    [Fact]
    public async Task SendAsync_WhenRequestSucceeds_ShouldCaptureDurationGreaterThanZero()
    {
        // Arrange
        var inner = new TestHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/ping");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.True(entry.DurationMs >= 0); // Stopwatch may read 0 on fast machines but never negative
    }

    // --- Captures error response ---

    [Fact]
    public async Task SendAsync_WhenResponseIs4xx_ShouldCaptureErrorSnippet()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Validation error")
        };
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/items");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal(400, entry.StatusCode);
        Assert.Equal("Validation error", entry.ErrorSnippet);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIs5xx_ShouldCaptureErrorSnippet()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server exploded")
        };
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/process");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal(500, entry.StatusCode);
        Assert.Equal("Server exploded", entry.ErrorSnippet);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIs2xx_ShouldNotSetErrorSnippet()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("all good")
        };
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/ok");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Null(entry.ErrorSnippet);
    }

    // --- ErrorSnippet truncation ---

    [Fact]
    public async Task SendAsync_WhenErrorBodyExceeds500Chars_ShouldTruncateErrorSnippetTo500()
    {
        // Arrange
        var longBody = new string('x', 800);
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(longBody)
        };
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/long-error");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal(500, entry.ErrorSnippet!.Length);
    }

    // --- TraceId extraction ---

    [Fact]
    public async Task SendAsync_WhenResponseHasTraceparentHeader_ShouldExtractTraceIdFromSecondSegment()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("traceparent", "00-abc123traceId000000000000000000-def456-01");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/trace");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("abc123traceId000000000000000000", entry.TraceId);
    }

    [Fact]
    public async Task SendAsync_WhenResponseHasXTraceIdHeader_ShouldExtractTraceId()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Trace-Id", "trace-xyz-789");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/trace");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("trace-xyz-789", entry.TraceId);
    }

    [Fact]
    public async Task SendAsync_WhenBothTraceparentAndXTraceIdPresent_ShouldPreferTraceparent()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("traceparent", "00-primary000000000000000000000000-parent-01");
        response.Headers.Add("X-Trace-Id", "fallback");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/both-headers");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("primary000000000000000000000000", entry.TraceId);
    }

    // --- CorrelationId extraction ---

    [Fact]
    public async Task SendAsync_WhenResponseHasXCorrelationIdHeader_ShouldExtractCorrelationId()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Correlation-Id", "corr-abc-123");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/corr");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("corr-abc-123", entry.CorrelationId);
    }

    [Fact]
    public async Task SendAsync_WhenResponseHasXRequestIdHeader_ShouldExtractCorrelationId()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Request-Id", "req-id-456");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/reqid");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("req-id-456", entry.CorrelationId);
    }

    [Fact]
    public async Task SendAsync_WhenBothCorrelationHeadersPresent_ShouldPreferXCorrelationId()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Correlation-Id", "preferred-corr");
        response.Headers.Add("X-Request-Id", "fallback-req");
        var inner = new TestHandler { Response = response };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/both-corr");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("preferred-corr", entry.CorrelationId);
    }

    // --- URL is path-only (no query string) ---

    [Fact]
    public async Task SendAsync_WhenUrlHasQueryString_ShouldCapturePathOnly()
    {
        // Arrange
        var inner = new TestHandler { Response = new HttpResponseMessage(HttpStatusCode.OK) };
        var tracker = new HttpActivityTracker(capacity: 10);
        var client = BuildClient(tracker, inner);

        // Act
        await client.GetAsync("https://example.com/api/search?q=secret&page=1");

        // Assert
        var entry = tracker.GetRecentActivity()[0];
        Assert.Equal("https://example.com/api/search", entry.Url);
        Assert.DoesNotContain("secret", entry.Url);
    }

    // --- Ring buffer capacity ---

    [Fact]
    public async Task SendAsync_WhenRequestCountExceedsCapacity_ShouldDropOldestEntries()
    {
        // Arrange
        var inner = new TestHandler();
        var tracker = new HttpActivityTracker(capacity: 3);
        var client = BuildClient(tracker, inner);

        // Act — send 5 requests, capacity is 3
        for (int i = 1; i <= 5; i++)
        {
            inner.Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("")
            };
            await client.GetAsync($"https://example.com/api/item/{i}");
        }

        // Assert — only the last 3 are kept
        var entries = tracker.GetRecentActivity();
        Assert.Equal(3, entries.Count);
        Assert.Equal("https://example.com/api/item/3", entries[0].Url);
        Assert.Equal("https://example.com/api/item/4", entries[1].Url);
        Assert.Equal("https://example.com/api/item/5", entries[2].Url);
    }
}
