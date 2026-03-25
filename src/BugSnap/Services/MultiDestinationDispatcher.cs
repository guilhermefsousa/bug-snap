using BugSnap.Destinations;
using BugSnap.Models;

namespace BugSnap.Services;

public sealed class MultiDestinationDispatcher(
    IEnumerable<IBugReportDestination> destinations,
    BugSnapOptions options)
{
    private readonly IReadOnlyList<IBugReportDestination> _destinations = destinations.ToList();

    public async Task<IReadOnlyList<BugReportResult>> DispatchAsync(
        BugReport report, CancellationToken ct = default)
    {
        PayloadSanitizer.Sanitize(report, options);

        var tasks = _destinations.Select(dest => SendToDestinationAsync(dest, report, ct));
        var results = await Task.WhenAll(tasks);
        return results;
    }

    private static async Task<BugReportResult> SendToDestinationAsync(
        IBugReportDestination destination, BugReport report, CancellationToken ct)
    {
        try
        {
            return await destination.SubmitAsync(report, ct);
        }
        catch (Exception ex)
        {
            return new BugReportResult(false, destination.Name, Error: ex.Message);
        }
    }
}
