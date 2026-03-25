using System.Text;
using System.Text.Json;
using BugSnap.Models;

namespace BugSnap.Destinations;

public class WebhookDestination : IBugReportDestination
{
    private readonly string _url;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookDestination(string url, IDictionary<string, string>? headers = null, int timeoutSeconds = 10)
    {
        _url = url;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };

        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                _client.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        }
    }

    public string Name => "Webhook";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(report, _jsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync(_url, content, ct);
            response.EnsureSuccessStatusCode();

            return new BugReportResult(true, Name);
        }
        catch (Exception ex)
        {
            return new BugReportResult(false, Name, Error: ex.Message);
        }
    }
}
