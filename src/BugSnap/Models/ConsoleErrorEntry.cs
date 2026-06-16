namespace BugSnap.Models;

public class ConsoleErrorEntry
{
    public string Message { get; set; } = "";
    public string? Stack { get; set; }
    public DateTime TimestampUtc { get; set; }
}
