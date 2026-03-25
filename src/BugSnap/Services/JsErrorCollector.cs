using System.Text.Json;
using BugSnap.Models;
using Microsoft.JSInterop;

namespace BugSnap.Services;

public sealed class JsErrorCollector(IJSRuntime js)
{
    public async Task InitAsync(int maxErrors)
        => await js.InvokeVoidAsync("window.__bugSnap.init", maxErrors);

    public async Task<List<JsErrorEntry>> GetErrorsAsync()
    {
        var raw = await js.InvokeAsync<JsonElement[]>("window.__bugSnap.getErrors");
        var result = new List<JsErrorEntry>(raw.Length);

        foreach (var el in raw)
        {
            var entry = new JsErrorEntry
            {
                Message = el.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                Source = el.TryGetProperty("source", out var src) ? src.GetString() : null,
                Line = el.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
                    ? line.GetInt32() : null,
                Column = el.TryGetProperty("column", out var col) && col.ValueKind == JsonValueKind.Number
                    ? col.GetInt32() : null,
                TimestampUtc = el.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                    ? DateTime.TryParse(ts.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
                    : DateTime.UtcNow
            };
            result.Add(entry);
        }

        return result;
    }

    public async Task<string> GetBrowserInfoAsync()
        => await js.InvokeAsync<string>("window.__bugSnap.getBrowserInfo");

    public async Task<string> GetScreenSizeAsync()
        => await js.InvokeAsync<string>("window.__bugSnap.getScreenSize");

    public async Task ClearErrorsAsync()
        => await js.InvokeVoidAsync("window.__bugSnap.clearErrors");
}
