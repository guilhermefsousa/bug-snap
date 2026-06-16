using System.Text.Json;
using BugSnap.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace BugSnap.Services;

public sealed class JsErrorCollector(IJSRuntime js) : IAsyncDisposable
{
    private DotNetObjectReference<JsErrorCollector>? _selfRef;

    // Resolved lazily to break the DI cycle:
    // AutoCaptureService → BugContextCollector → JsErrorCollector
    // We resolve AutoCaptureService here via IServiceProvider at callback time.
    private AutoCaptureService? _autoCapture;

    public async Task InitAsync(int maxErrors)
        => await js.InvokeVoidAsync("window.__bugSnap.init", maxErrors);

    public async Task<List<JsErrorEntry>> GetErrorsAsync()
    {
        var raw = await js.InvokeAsync<JsonElement[]>("window.__bugSnap.getErrors");
        var result = new List<JsErrorEntry>(raw.Length);

        foreach (var el in raw)
        {
            result.Add(MapElement(el));
        }

        return result;
    }

    /// <summary>
    /// Registers this instance as a .NET callback target for JS-side auto-capture.
    /// Safe to call multiple times — only registers once.
    /// </summary>
    public async Task RegisterAutoCaptureCallbackAsync(IServiceProvider sp)
    {
        _selfRef ??= DotNetObjectReference.Create(this);
        _autoCapture = sp.GetService<AutoCaptureService>();
        await js.InvokeVoidAsync("window.__bugSnap.initAutoCapture", _selfRef);
    }

    [JSInvokable("OnJsError")]
    public async Task OnJsErrorAsync(JsonElement entry)
    {
        if (_autoCapture is null) return;
        var jsError = MapElement(entry);
        await _autoCapture.ReportAsync(null, jsError, "js", default);
    }

    public async Task<string> GetBrowserInfoAsync()
        => await js.InvokeAsync<string>("window.__bugSnap.getBrowserInfo");

    public async Task<string> GetScreenSizeAsync()
        => await js.InvokeAsync<string>("window.__bugSnap.getScreenSize");

    public async Task<MemoryInfo> GetMemoryInfoAsync()
    {
        var raw = await js.InvokeAsync<JsonElement>("window.__bugSnap.getMemoryInfo");
        return new MemoryInfo
        {
            JsHeapUsedBytes = ReadLong(raw, "jsHeapUsedBytes"),
            JsHeapTotalBytes = ReadLong(raw, "jsHeapTotalBytes"),
            JsHeapLimitBytes = ReadLong(raw, "jsHeapLimitBytes")
        };
    }

    public async Task<List<ConsoleErrorEntry>> GetConsoleErrorsAsync()
    {
        var raw = await js.InvokeAsync<JsonElement[]>("window.__bugSnap.getConsoleErrors");
        var result = new List<ConsoleErrorEntry>(raw.Length);
        foreach (var el in raw)
        {
            result.Add(new ConsoleErrorEntry
            {
                Message = el.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
                Stack = el.TryGetProperty("stack", out var stack) ? stack.GetString() : null,
                TimestampUtc = ReadTimestamp(el)
            });
        }
        return result;
    }

    public async Task<List<BreadcrumbEntry>> GetBreadcrumbsAsync()
    {
        var raw = await js.InvokeAsync<JsonElement[]>("window.__bugSnap.getBreadcrumbs");
        var result = new List<BreadcrumbEntry>(raw.Length);
        foreach (var el in raw)
        {
            result.Add(new BreadcrumbEntry
            {
                Type = el.TryGetProperty("type", out var type) ? type.GetString() ?? "" : "",
                Detail = el.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
                TimestampUtc = ReadTimestamp(el)
            });
        }
        return result;
    }

    public async Task ClearErrorsAsync()
        => await js.InvokeVoidAsync("window.__bugSnap.clearErrors");

    private static long? ReadLong(JsonElement el, string property)
        => el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(property, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var value)
            ? value
            : null;

    private static DateTime ReadTimestamp(JsonElement el)
        => el.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
            ? DateTime.TryParse(ts.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
            : DateTime.UtcNow;

    private static JsErrorEntry MapElement(JsonElement el) => new()
    {
        Message = el.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "",
        Source = el.TryGetProperty("source", out var src) ? src.GetString() : null,
        Line = el.TryGetProperty("line", out var line) && line.ValueKind == JsonValueKind.Number
            ? line.GetInt32() : null,
        Column = el.TryGetProperty("column", out var col) && col.ValueKind == JsonValueKind.Number
            ? col.GetInt32() : null,
        StackTrace = el.TryGetProperty("stackTrace", out var st) ? st.GetString() : null,
        TimestampUtc = el.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
            ? DateTime.TryParse(ts.GetString(), out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow
            : DateTime.UtcNow
    };

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        _selfRef = null;
        await ValueTask.CompletedTask;
    }
}
