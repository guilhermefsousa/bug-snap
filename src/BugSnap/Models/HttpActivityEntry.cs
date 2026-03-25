namespace BugSnap.Models;

public class HttpActivityEntry
{
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string? ErrorSnippet { get; set; }
    public string? TraceId { get; set; }
    public string? CorrelationId { get; set; }
}
