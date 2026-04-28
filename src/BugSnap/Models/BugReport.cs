namespace BugSnap.Models;

public class BugReport
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string SdkVersion { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public BugSnapSeverity Severity { get; set; } = BugSnapSeverity.Medium;
    public BugSnapCategory Category { get; set; } = BugSnapCategory.Other;
    public byte[]? Screenshot { get; set; }
    public string? ScreenshotFileName { get; set; }
    public BugContextSnapshot Context { get; set; } = new();
    public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Fingerprint { get; set; }
    public bool AutoDetected { get; set; } = false;
}
