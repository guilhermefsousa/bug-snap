using BugSnap.Models;

namespace BugSnap.Destinations;

public interface IBugReportDestination
{
    string Name { get; }
    Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default);
}
