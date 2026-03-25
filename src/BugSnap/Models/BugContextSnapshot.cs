namespace BugSnap.Models;

public class BugContextSnapshot
{
    public string CurrentRoute { get; set; } = "";
    public string BrowserInfo { get; set; } = "";
    public string ScreenSize { get; set; } = "";
    public string? SignalRState { get; set; }
    public string? AppName { get; set; }
    public string? AppVersion { get; set; }
    public string? Environment { get; set; }
    public DateTime CollectedAtUtc { get; set; } = DateTime.UtcNow;
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? SessionId { get; set; }
    public string PageInstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<HttpActivityEntry> RecentRequests { get; set; } = [];
    public IReadOnlyList<JsErrorEntry> RecentJsErrors { get; set; } = [];
    public IDictionary<string, string> CustomContext { get; set; } = new Dictionary<string, string>();
    public BugSnapCategory? SuggestedCategory { get; set; }
}
