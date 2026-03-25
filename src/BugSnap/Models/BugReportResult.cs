namespace BugSnap.Models;

public record BugReportResult(bool Success, string DestinationName, string? Url = null, string? Error = null);
