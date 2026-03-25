using System.Diagnostics;
using BugSnap.Models;

namespace BugSnap.Services;

public sealed class HttpActivityTracker : DelegatingHandler
{
    private readonly RingBuffer<HttpActivityEntry> _buffer;

    public HttpActivityTracker(int capacity = 20)
    {
        _buffer = new RingBuffer<HttpActivityEntry>(capacity);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var timestamp = DateTime.UtcNow;

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Record failed requests — these are the most useful for debugging
            _buffer.Add(new HttpActivityEntry
            {
                Method = request.Method.Method,
                Url = BuildSafeUrl(request.RequestUri),
                StatusCode = 0,
                DurationMs = sw.ElapsedMilliseconds,
                TimestampUtc = timestamp,
                ErrorSnippet = $"{ex.GetType().Name}: {Truncate(ex.Message, 500)}"
            });
            throw;
        }

        sw.Stop();

        var url = BuildSafeUrl(request.RequestUri);
        string? traceId = ExtractTraceId(response);
        string? correlationId = ExtractCorrelationId(response);
        string? errorSnippet = null;

        int status = (int)response.StatusCode;
        if (status >= 400)
        {
            try
            {
                // ReadAsStringAsync is safe in Blazor WASM (HttpContent buffers internally).
                // The content remains readable by the caller after this read.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                errorSnippet = Truncate(body, 500);
            }
            catch
            {
                // ignore — reading body is best-effort
            }
        }

        _buffer.Add(new HttpActivityEntry
        {
            Method = request.Method.Method,
            Url = url,
            StatusCode = status,
            DurationMs = sw.ElapsedMilliseconds,
            TimestampUtc = timestamp,
            ErrorSnippet = errorSnippet,
            TraceId = traceId,
            CorrelationId = correlationId
        });

        return response;
    }

    public IReadOnlyList<HttpActivityEntry> GetRecentActivity() => _buffer.ToList();

    private static string BuildSafeUrl(Uri? uri)
    {
        if (uri is null) return "";
        return uri.GetLeftPart(UriPartial.Path);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length > maxLength ? value[..maxLength] : value;

    private static string? ExtractTraceId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("traceparent", out var traceparentValues))
        {
            var value = traceparentValues.FirstOrDefault();
            if (value is not null)
            {
                var parts = value.Split('-');
                if (parts.Length >= 3) return parts[1];
            }
        }

        if (response.Headers.TryGetValues("X-Trace-Id", out var traceIdValues))
            return traceIdValues.FirstOrDefault();

        return null;
    }

    private static string? ExtractCorrelationId(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Correlation-Id", out var corrValues))
            return corrValues.FirstOrDefault();

        if (response.Headers.TryGetValues("X-Request-Id", out var reqIdValues))
            return reqIdValues.FirstOrDefault();

        return null;
    }
}
