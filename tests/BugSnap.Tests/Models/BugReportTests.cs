using BugSnap.Models;

namespace BugSnap.Tests.Models;

public class BugReportTests
{
    // --- Default values ---

    [Fact]
    public void BugReport_WhenCreated_ShouldHaveSchemaVersion100()
    {
        // Arrange & Act
        var report = new BugReport();

        // Assert
        Assert.Equal("1.0.0", report.SchemaVersion);
    }

    [Fact]
    public void BugReport_WhenCreated_ShouldHaveNonEmptyReportId()
    {
        // Arrange & Act
        var report = new BugReport();

        // Assert
        Assert.False(string.IsNullOrEmpty(report.ReportId));
    }

    [Fact]
    public void BugReport_WhenCreated_ShouldHaveUniqueReportIds()
    {
        // Arrange & Act
        var report1 = new BugReport();
        var report2 = new BugReport();

        // Assert
        Assert.NotEqual(report1.ReportId, report2.ReportId);
    }

    [Fact]
    public void BugReport_WhenCreated_ShouldHaveCreatedAtUtcWithinReasonableRange()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var report = new BugReport();

        var after = DateTime.UtcNow.AddSeconds(1);

        // Assert
        Assert.True(report.CreatedAtUtc >= before, "CreatedAtUtc should be at or after test start");
        Assert.True(report.CreatedAtUtc <= after, "CreatedAtUtc should be at or before test end");
    }

    // --- Severity default ---

    [Fact]
    public void BugReport_WhenCreated_ShouldDefaultSeverityToMedium()
    {
        // Arrange & Act
        var report = new BugReport();

        // Assert
        Assert.Equal(BugSnapSeverity.Medium, report.Severity);
    }

    // --- Category default ---

    [Fact]
    public void BugReport_WhenCreated_ShouldDefaultCategoryToOther()
    {
        // Arrange & Act
        var report = new BugReport();

        // Assert
        Assert.Equal(BugSnapCategory.Other, report.Category);
    }

    // --- Context default ---

    [Fact]
    public void BugReport_WhenCreated_ShouldHaveNonNullContext()
    {
        // Arrange & Act
        var report = new BugReport();

        // Assert
        Assert.NotNull(report.Context);
    }
}
