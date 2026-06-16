using System.Text.Json;
using BugSnap.Models;
using BugSnap.Services;
using Microsoft.JSInterop;

namespace BugSnap.Tests.Services;

/// <summary>
/// Verifies that JsErrorCollector correctly maps the JSON returned by the
/// vanilla JS interop (getMemoryInfo / getConsoleErrors / getBreadcrumbs) onto
/// the strongly-typed models. Uses a hand-rolled fake IJSRuntime — no mock libs.
/// </summary>
public class JsErrorCollectorMappingTests
{
    /// <summary>
    /// Fake IJSRuntime that returns a pre-serialized JSON payload per identifier.
    /// The runtime deserializes the JSON to the requested TValue, mirroring what
    /// the real JS interop bridge does.
    /// </summary>
    private sealed class FakeJsRuntime(IReadOnlyDictionary<string, string> jsonByIdentifier) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (!jsonByIdentifier.TryGetValue(identifier, out var json))
                throw new InvalidOperationException($"No fake configured for '{identifier}'.");

            var value = JsonSerializer.Deserialize<TValue>(json)!;
            return ValueTask.FromResult(value);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }

    private static JsErrorCollector CollectorReturning(string identifier, string json)
        => new(new FakeJsRuntime(new Dictionary<string, string> { [identifier] = json }));

    // --- MemoryInfo mapping ---

    [Fact]
    public async Task GetMemoryInfoAsync_WhenChromiumValuesPresent_ShouldMapAllThreeJsHeapFields()
    {
        // Arrange
        var collector = CollectorReturning(
            "window.__bugSnap.getMemoryInfo",
            """{"jsHeapUsedBytes":1048576,"jsHeapTotalBytes":2097152,"jsHeapLimitBytes":4194304}""");

        // Act
        var memory = await collector.GetMemoryInfoAsync();

        // Assert
        Assert.Equal(1048576, memory.JsHeapUsedBytes);
        Assert.Equal(2097152, memory.JsHeapTotalBytes);
        Assert.Equal(4194304, memory.JsHeapLimitBytes);
    }

    [Fact]
    public async Task GetMemoryInfoAsync_WhenFirefoxReturnsNulls_ShouldMapJsHeapFieldsToNull()
    {
        // Arrange — Firefox/Safari do not expose performance.memory; JS returns nulls.
        var collector = CollectorReturning(
            "window.__bugSnap.getMemoryInfo",
            """{"jsHeapUsedBytes":null,"jsHeapTotalBytes":null,"jsHeapLimitBytes":null}""");

        // Act
        var memory = await collector.GetMemoryInfoAsync();

        // Assert
        Assert.Null(memory.JsHeapUsedBytes);
        Assert.Null(memory.JsHeapTotalBytes);
        Assert.Null(memory.JsHeapLimitBytes);
        // ManagedHeapBytes is filled in by the collector (GC), not the JS interop.
        Assert.Null(memory.ManagedHeapBytes);
    }

    // --- ConsoleErrorEntry mapping ---

    [Fact]
    public async Task GetConsoleErrorsAsync_WhenEntriesPresent_ShouldMapMessageStackAndTimestamp()
    {
        // Arrange
        var collector = CollectorReturning(
            "window.__bugSnap.getConsoleErrors",
            """[{"message":"boom","stack":"at x.js:1","timestamp":"2026-06-16T10:00:00.000Z"}]""");

        // Act
        var errors = await collector.GetConsoleErrorsAsync();

        // Assert
        var entry = Assert.Single(errors);
        Assert.Equal("boom", entry.Message);
        Assert.Equal("at x.js:1", entry.Stack);
        Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.Kind);
        Assert.Equal(new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc), entry.TimestampUtc);
    }

    [Fact]
    public async Task GetConsoleErrorsAsync_WhenStackMissing_ShouldMapStackToNull()
    {
        // Arrange
        var collector = CollectorReturning(
            "window.__bugSnap.getConsoleErrors",
            """[{"message":"no stack","stack":null,"timestamp":"2026-06-16T10:00:00.000Z"}]""");

        // Act
        var errors = await collector.GetConsoleErrorsAsync();

        // Assert
        var entry = Assert.Single(errors);
        Assert.Equal("no stack", entry.Message);
        Assert.Null(entry.Stack);
    }

    // --- BreadcrumbEntry mapping ---

    [Fact]
    public async Task GetBreadcrumbsAsync_WhenEntriesPresent_ShouldMapTypeDetailAndTimestamp()
    {
        // Arrange
        var collector = CollectorReturning(
            "window.__bugSnap.getBreadcrumbs",
            """
            [
              {"type":"navigation","detail":"/tickets","timestamp":"2026-06-16T10:00:00.000Z"},
              {"type":"click","detail":"open-settings","timestamp":"2026-06-16T10:00:05.000Z"}
            ]
            """);

        // Act
        var crumbs = await collector.GetBreadcrumbsAsync();

        // Assert
        Assert.Equal(2, crumbs.Count);
        Assert.Equal("navigation", crumbs[0].Type);
        Assert.Equal("/tickets", crumbs[0].Detail);
        Assert.Equal("click", crumbs[1].Type);
        Assert.Equal("open-settings", crumbs[1].Detail);
        Assert.Equal(DateTimeKind.Utc, crumbs[0].TimestampUtc.Kind);
    }

    [Fact]
    public async Task GetBreadcrumbsAsync_WhenEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var collector = CollectorReturning("window.__bugSnap.getBreadcrumbs", "[]");

        // Act
        var crumbs = await collector.GetBreadcrumbsAsync();

        // Assert
        Assert.Empty(crumbs);
    }
}
