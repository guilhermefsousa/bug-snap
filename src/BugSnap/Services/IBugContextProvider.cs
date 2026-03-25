namespace BugSnap.Services;

public interface IBugContextProvider
{
    Task<IDictionary<string, string>> GetCustomContextAsync(CancellationToken ct = default);
}
