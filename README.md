# BugSnap

[![NuGet](https://img.shields.io/nuget/v/BugSnap.svg)](https://www.nuget.org/packages/BugSnap)

Structured bug capture library for Blazor apps. Auto-collects context (route, browser, HTTP history, JS errors, correlation IDs), sanitizes sensitive data client-side, and sends to pluggable destinations.

**BugSnap captures ~85% of the context automatically.** The user only describes what happened.

> **Design principle:** BugSnap is infrastructure — it captures and sends. The UI is your app's responsibility. Each app has its own design system, theme, and components. BugSnap provides the services, your app provides the interface.

---

## Install

```bash
dotnet add package BugSnap
```

Or add to your `.csproj`:

```xml
<PackageReference Include="BugSnap" Version="1.1.*" />
```

---

## Quick Start

### 1. Register services in `Program.cs`

```csharp
using BugSnap.Extensions;
using BugSnap.Destinations;

builder.Services.AddBugSnap(options =>
{
    options.AppName = "MyApp";
    options.AppVersion = "1.0.0";
    options.Environment = "Production";
    options.Destinations.Add(new WebhookDestination("https://your-api.com/bugs"));
});
```

### 2. Wire HTTP tracking into your HttpClient

```csharp
builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri("https://your-api.com");
})
.AddHttpMessageHandler<HttpActivityTracker>();
```

### 3. Add the JS reference to `index.html`

```html
<script src="_content/BugSnap/bug-snap.js"></script>
```

### 4. Build your own UI (recommended)

BugSnap provides services, not UI. Create a dialog in your app's design system:

```razor
@using BugSnap.Models
@using BugSnap.Services

@inject BugContextCollector Collector
@inject MultiDestinationDispatcher Dispatcher

<button @onclick="OpenDialog">Report Bug</button>

@if (_open)
{
    <!-- Your modal/dialog component here -->
    <textarea @bind="_description" placeholder="What happened?" />
    <button @onclick="Submit">Send</button>
}

@code {
    private bool _open;
    private string _description = "";
    private BugContextSnapshot? _context;

    private async Task OpenDialog()
    {
        _open = true;
        _context = await Collector.CollectAsync(); // auto-collects everything
    }

    private async Task Submit()
    {
        var report = new BugReport
        {
            Title = _description.Length <= 50 ? _description : _description[..50],
            Description = _description,
            Severity = _context?.RecentRequests.Any(r => r.StatusCode >= 500) == true
                ? BugSnapSeverity.High : BugSnapSeverity.Low,
            Category = _context?.SuggestedCategory ?? BugSnapCategory.Other,
            Fingerprint = FingerprintGenerator.Generate(_context ?? new()),
            Context = _context ?? new()
        };

        var results = await Dispatcher.DispatchAsync(report);

        if (results.Any(r => r.Success))
        {
            // Show success toast
            _open = false;
        }
    }
}
```

### 5. Or use the built-in components (basic)

BugSnap includes basic components if you don't need custom UI:

```razor
@using BugSnap.Components

<BugReportButton />
```

---

## Configuration

### BugSnapOptions

| Property | Default | Description |
|----------|---------|-------------|
| `AppName` | `null` | Your app name (included in reports) |
| `AppVersion` | `null` | Your app version |
| `Environment` | `null` | Environment name (Production, Staging, etc.) |
| `MaxHttpEntries` | `20` | Max HTTP requests to keep in ring buffer |
| `MaxJsErrors` | `10` | Max JS errors to keep in buffer |
| `MaxErrorSnippetLength` | `500` | Max chars for HTTP error response snippets |
| `RateLimitSeconds` | `30` | Min seconds between bug reports (prevents spam) |
| `Destinations` | `[]` | List of destinations to send reports to |
| `EnableConsoleDestination` | `false` | Log reports to browser console (dev/testing) |

---

## Destinations

BugSnap sends to **multiple destinations simultaneously**. One failure doesn't block others.

### WebhookDestination

POSTs the full `BugReport` as JSON to any URL.

```csharp
// Simple
new WebhookDestination("https://your-api.com/bugs")

// With custom headers and timeout
new WebhookDestination(
    "https://your-api.com/bugs",
    headers: new Dictionary<string, string> { ["X-Api-Key"] = "secret" },
    timeoutSeconds: 15
)
```

### GitHubIssueDestination

Creates a GitHub Issue with structured markdown body.

```csharp
new GitHubIssueDestination("your-org/your-repo", "ghp_your_token",
    labels: ["bug", "auto-triage"])
```

### ConsoleDestination

Logs to browser console (dev/testing only):

```csharp
options.EnableConsoleDestination = true;
```

### Custom Destination

```csharp
public class SlackDestination : IBugReportDestination
{
    public string Name => "Slack";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        // POST to Slack webhook...
        return new BugReportResult(true, Name);
    }
}
```

### Authenticated Destination (for apps with auth)

If your API requires authentication, create a destination that uses your app's HttpClient:

```csharp
public sealed class AuthenticatedBugReportDestination(IHttpClientFactory factory) : IBugReportDestination
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string Name => "Api";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        try
        {
            var client = factory.CreateClient("YourAuthenticatedClient");
            var response = await client.PostAsJsonAsync("api/bug-reports", report, _json, ct);
            response.EnsureSuccessStatusCode();
            return new BugReportResult(true, Name);
        }
        catch (Exception ex)
        {
            return new BugReportResult(false, Name, Error: ex.Message);
        }
    }
}

// Register via DI (not in options.Destinations — needs IHttpClientFactory from DI)
builder.Services.AddScoped<IBugReportDestination, AuthenticatedBugReportDestination>();
```

---

## App-Specific Context

Inject domain-specific data via `IBugContextProvider`:

```csharp
public class MyAppBugContextProvider : IBugContextProvider
{
    public Task<IDictionary<string, string>> GetCustomContextAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>
        {
            ["UserId"] = "...",
            ["TenantId"] = "...",
            ["ActiveModule"] = "...",
        });
    }
}

// Register after AddBugSnap()
builder.Services.AddScoped<IBugContextProvider, MyAppBugContextProvider>();
```

---

## Fingerprint (Deduplication)

BugSnap generates a fingerprint hash for each report, enabling backend deduplication. The fingerprint is a 16-character hex string (SHA256) based on:

- **Normalized route** — query params stripped, GUIDs/numeric IDs replaced with `{id}`
- **Error signature** — first JS error message (truncated) OR first HTTP error (status + normalized URL)
- **App version**

```csharp
var fingerprint = FingerprintGenerator.Generate(context);
// "a1b2c3d4e5f6a7b8"
```

Same bug, same route, same error class = same fingerprint — regardless of entity IDs in the URL.

---

## Category Auto-Suggestion

BugSnap suggests a bug category based on collected evidence:

| Priority | Condition | Category |
|----------|-----------|----------|
| 1 | HTTP 401/403 | Auth |
| 2 | HTTP 5xx | API |
| 3 | SignalR disconnected | SignalR |
| 4 | JS errors present | UI |
| 5 | HTTP 4xx | API |
| 6 | Requests > 3 seconds | Performance |
| 7 | None of the above | Other |

The suggestion is available in `context.SuggestedCategory` after calling `CollectAsync()`.

---

## What Gets Captured Automatically

| Data | Source |
|------|--------|
| Current route | `NavigationManager.Uri` |
| Browser / OS | `navigator.userAgent` |
| Screen size | `window.innerWidth x innerHeight` |
| Recent HTTP requests (last 20) | `HttpActivityTracker` DelegatingHandler |
| HTTP error snippets (first 500 chars) | Response body on 4xx/5xx |
| Failed requests (network errors) | Exception type + message |
| TraceId | `traceparent` or `X-Trace-Id` response header |
| CorrelationId | `X-Correlation-Id` or `X-Request-Id` response header |
| JS errors | `window.error` + `unhandledrejection` events |
| App name / version / env | `BugSnapOptions` config |
| Custom context | `IBugContextProvider` implementation |

---

## Sanitization

BugSnap sanitizes sensitive data **before** it leaves the browser. Not optional.

| Pattern | Action |
|---------|--------|
| `Authorization: Bearer xxx` | `Bearer [REDACTED]` |
| `Authorization: Basic xxx` | `Basic [REDACTED]` |
| `Cookie: xxx` / `Set-Cookie: xxx` | `Cookie: [REDACTED]` |
| `X-Api-Key: xxx` / `X-Token: xxx` | `X-Api-Key: [REDACTED]` |
| `?token=xxx`, `?key=xxx`, `?api_key=xxx` | `?token=[REDACTED]` |
| Error snippets | Truncated to 500 chars |

---

## Architecture

```
Your Blazor App
    |
    +-- Your custom UI (dialog/modal/button)
    |       |
    |       +-- BugContextCollector
    |       |       |
    |       |       +-- HttpActivityBuffer (singleton — ring buffer of last 20 requests)
    |       |       +-- HttpActivityTracker (transient — DelegatingHandler in HttpClient pipeline)
    |       |       +-- JsErrorCollector (JS interop: errors, browser, screen)
    |       |       +-- IBugContextProvider (your app-specific context)
    |       |       +-- CategorySuggester (auto-suggests category from context)
    |       |
    |       +-- FingerprintGenerator (SHA256 hash for dedup)
    |       +-- PayloadSanitizer (masks tokens/headers before send)
    |       |
    |       +-- MultiDestinationDispatcher
    |               |
    |               +-- WebhookDestination
    |               +-- GitHubIssueDestination
    |               +-- ConsoleDestination
    |               +-- Your custom destination
```

---

## Requirements

- .NET 10+
- Blazor WebAssembly or Blazor Server
- No external JS dependencies
- No NuGet dependencies beyond `Microsoft.AspNetCore.Components.Web`

---

## License

MIT
