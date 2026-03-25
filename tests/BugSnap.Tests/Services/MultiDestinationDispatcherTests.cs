using BugSnap;
using BugSnap.Destinations;
using BugSnap.Models;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class MultiDestinationDispatcherTests
{
    // --- Fake destination ---

    private class FakeDestination : IBugReportDestination
    {
        public string Name { get; }
        public bool ShouldFail { get; set; }
        public BugReport? ReceivedReport { get; private set; }

        public FakeDestination(string name = "Fake") => Name = name;

        public Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
        {
            ReceivedReport = report;
            if (ShouldFail) throw new Exception("Destination failed");
            return Task.FromResult(new BugReportResult(true, Name));
        }
    }

    private static BugReport EmptyReport() => new() { Title = "Test bug" };

    private static BugSnapOptions DefaultOptions() => new() { MaxErrorSnippetLength = 500 };

    // --- Single destination ---

    [Fact]
    public async Task DispatchAsync_WhenSingleDestinationSucceeds_ShouldReturnSuccessResult()
    {
        // Arrange
        var dest = new FakeDestination("Alpha");
        var dispatcher = new MultiDestinationDispatcher([dest], DefaultOptions());

        // Act
        var results = await dispatcher.DispatchAsync(EmptyReport());

        // Assert
        Assert.Single(results);
        Assert.True(results[0].Success);
        Assert.Equal("Alpha", results[0].DestinationName);
    }

    // --- Multiple destinations ---

    [Fact]
    public async Task DispatchAsync_WhenMultipleDestinations_ShouldReturnResultPerDestination()
    {
        // Arrange
        var dest1 = new FakeDestination("Alpha");
        var dest2 = new FakeDestination("Beta");
        var dest3 = new FakeDestination("Gamma");
        var dispatcher = new MultiDestinationDispatcher([dest1, dest2, dest3], DefaultOptions());

        // Act
        var results = await dispatcher.DispatchAsync(EmptyReport());

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.DestinationName == "Alpha");
        Assert.Contains(results, r => r.DestinationName == "Beta");
        Assert.Contains(results, r => r.DestinationName == "Gamma");
    }

    [Fact]
    public async Task DispatchAsync_WhenMultipleDestinations_ShouldDeliverReportToEach()
    {
        // Arrange
        var dest1 = new FakeDestination("Alpha");
        var dest2 = new FakeDestination("Beta");
        var dispatcher = new MultiDestinationDispatcher([dest1, dest2], DefaultOptions());
        var report = EmptyReport();

        // Act
        await dispatcher.DispatchAsync(report);

        // Assert
        Assert.NotNull(dest1.ReceivedReport);
        Assert.NotNull(dest2.ReceivedReport);
    }

    // --- Failure isolation ---

    [Fact]
    public async Task DispatchAsync_WhenOneDestinationFails_ShouldNotBlockOthers()
    {
        // Arrange
        var failing = new FakeDestination("Failing") { ShouldFail = true };
        var succeeding = new FakeDestination("Succeeding");
        var dispatcher = new MultiDestinationDispatcher([failing, succeeding], DefaultOptions());

        // Act
        var results = await dispatcher.DispatchAsync(EmptyReport());

        // Assert — both results returned, failing one has Success=false
        Assert.Equal(2, results.Count);
        var failResult = results.First(r => r.DestinationName == "Failing");
        var okResult = results.First(r => r.DestinationName == "Succeeding");
        Assert.False(failResult.Success);
        Assert.Equal("Destination failed", failResult.Error);
        Assert.True(okResult.Success);
    }

    [Fact]
    public async Task DispatchAsync_WhenAllDestinationsFail_ShouldReturnAllFailures()
    {
        // Arrange
        var dest1 = new FakeDestination("A") { ShouldFail = true };
        var dest2 = new FakeDestination("B") { ShouldFail = true };
        var dispatcher = new MultiDestinationDispatcher([dest1, dest2], DefaultOptions());

        // Act
        var results = await dispatcher.DispatchAsync(EmptyReport());

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.Success));
    }

    // --- Sanitization runs before dispatch ---

    [Fact]
    public async Task DispatchAsync_WhenErrorSnippetExceedsMaxLength_ShouldTruncateBeforeDispatch()
    {
        // Arrange
        var longSnippet = new string('x', 800);
        var entry = new HttpActivityEntry { Url = "https://example.com", ErrorSnippet = longSnippet };
        var report = new BugReport();
        report.Context.RecentRequests = [entry];

        var dest = new FakeDestination();
        var options = new BugSnapOptions { MaxErrorSnippetLength = 100 };
        var dispatcher = new MultiDestinationDispatcher([dest], options);

        // Act
        await dispatcher.DispatchAsync(report);

        // Assert — destination received already-sanitized report
        Assert.NotNull(dest.ReceivedReport);
        var receivedEntry = dest.ReceivedReport!.Context.RecentRequests[0];
        Assert.Equal(100, receivedEntry.ErrorSnippet!.Length);
    }

    [Fact]
    public async Task DispatchAsync_WhenUrlHasSensitiveQueryParam_ShouldSanitizeBeforeDispatch()
    {
        // Arrange
        var entry = new HttpActivityEntry { Url = "https://example.com/api?token=secret123" };
        var report = new BugReport();
        report.Context.RecentRequests = [entry];

        var dest = new FakeDestination();
        var dispatcher = new MultiDestinationDispatcher([dest], DefaultOptions());

        // Act
        await dispatcher.DispatchAsync(report);

        // Assert
        var receivedEntry = dest.ReceivedReport!.Context.RecentRequests[0];
        Assert.Contains("token=[REDACTED]", receivedEntry.Url);
        Assert.DoesNotContain("secret123", receivedEntry.Url);
    }

    // --- No destinations ---

    [Fact]
    public async Task DispatchAsync_WhenNoDestinations_ShouldReturnEmptyList()
    {
        // Arrange
        var dispatcher = new MultiDestinationDispatcher([], DefaultOptions());

        // Act
        var results = await dispatcher.DispatchAsync(EmptyReport());

        // Assert
        Assert.Empty(results);
    }
}
