namespace BugSnap.Services;

public class DefaultBugContextProvider : IBugContextProvider
{
    public Task<IDictionary<string, string>> GetCustomContextAsync(CancellationToken ct = default)
        => Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>());
}
