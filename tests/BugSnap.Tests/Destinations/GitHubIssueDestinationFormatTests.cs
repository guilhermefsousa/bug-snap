using BugSnap.Destinations;
using BugSnap.Models;

namespace BugSnap.Tests.Destinations;

/// <summary>
/// Tests the markdown rendering of the new capture fields (memory, console
/// errors, breadcrumbs) in the GitHub issue body. FormatMarkdown is internal
/// and reachable via InternalsVisibleTo.
/// </summary>
public class GitHubIssueDestinationFormatTests
{
    private static BugReport BaseReport() => new()
    {
        Title = "x",
        Description = "y",
        Context = new BugContextSnapshot { CurrentRoute = "/home" }
    };

    [Fact]
    public void FormatMarkdown_WhenMemoryPresent_ShouldRenderHeapRowInMb()
    {
        // Arrange
        var report = BaseReport();
        report.Context.Memory = new MemoryInfo
        {
            ManagedHeapBytes = 10 * 1024 * 1024, // 10 MB
            JsHeapUsedBytes = 5 * 1024 * 1024     // 5 MB
        };

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.Contains("Heap (managed/js)", md);
        Assert.Contains("10 MB", md);
        Assert.Contains("5 MB", md);
    }

    [Fact]
    public void FormatMarkdown_WhenJsHeapNull_ShouldRenderNaForJsButManagedValue()
    {
        // Arrange — Firefox path: JS heap null, managed heap from GC present.
        var report = BaseReport();
        report.Context.Memory = new MemoryInfo
        {
            ManagedHeapBytes = 12 * 1024 * 1024,
            JsHeapUsedBytes = null
        };

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.Contains("12 MB / n/a", md);
    }

    [Fact]
    public void FormatMarkdown_WhenMemoryAbsent_ShouldNotRenderHeapRow()
    {
        // Arrange
        var report = BaseReport();

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.DoesNotContain("Heap (managed/js)", md);
    }

    [Fact]
    public void FormatMarkdown_WhenConsoleErrorsPresent_ShouldRenderDetailsSection()
    {
        // Arrange
        var report = BaseReport();
        report.Context.RecentConsoleErrors =
        [
            new ConsoleErrorEntry { Message = "render boom", Stack = "at app.js:42" }
        ];

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.Contains("### Console Errors", md);
        Assert.Contains("<details>", md);
        Assert.Contains("render boom", md);
        Assert.Contains("at app.js:42", md);
    }

    [Fact]
    public void FormatMarkdown_WhenBreadcrumbsPresent_ShouldRenderTrail()
    {
        // Arrange
        var report = BaseReport();
        report.Context.Breadcrumbs =
        [
            new BreadcrumbEntry { Type = "navigation", Detail = "/tickets" },
            new BreadcrumbEntry { Type = "click", Detail = "open-settings" }
        ];

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.Contains("### Breadcrumbs", md);
        Assert.Contains("navigation", md);
        Assert.Contains("/tickets", md);
        Assert.Contains("click", md);
        Assert.Contains("open-settings", md);
    }

    [Fact]
    public void FormatMarkdown_WhenNoNewFields_ShouldNotRenderNewSections()
    {
        // Arrange
        var report = BaseReport();

        // Act
        var md = GitHubIssueDestination.FormatMarkdown(report);

        // Assert
        Assert.DoesNotContain("### Console Errors", md);
        Assert.DoesNotContain("### Breadcrumbs", md);
    }
}
