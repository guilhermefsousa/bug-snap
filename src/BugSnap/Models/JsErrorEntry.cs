namespace BugSnap.Models;

public class JsErrorEntry
{
    public string Message { get; set; } = "";
    public string? Source { get; set; }
    public int? Line { get; set; }
    public int? Column { get; set; }
    public DateTime TimestampUtc { get; set; }
}
