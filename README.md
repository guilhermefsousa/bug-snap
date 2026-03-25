# BugSnap

Structured bug capture library for Blazor apps. Auto-collects context (route, browser, HTTP history, JS errors, correlation IDs), sanitizes sensitive data client-side, shows a preview to the user, and sends to pluggable destinations.

**BugSnap captures ~85% of the context automatically.** The user only describes what happened.

---

## Quick Start

### 1. Add the project reference

```xml
<ProjectReference Include="path/to/BugSnap.csproj" />
```

> NuGet package coming soon.

### 2. Register services in `Program.cs`

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

### 3. Wire HTTP tracking into your HttpClient

This is required for BugSnap to capture HTTP request/response history:

```csharp
using BugSnap.Services;

builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri("https://your-api.com");
})
.AddHttpMessageHandler(sp => sp.GetRequiredService<HttpActivityTracker>());
```

### 4. Add the JS reference to `index.html`

```html
<script src="_content/BugSnap/bug-snap.js"></script>
```

### 5. Add the button to your layout

```razor
@using BugSnap.Components

<BugReportButton />
```

That's it. Your users now have a bug report button that auto-collects context, sanitizes sensitive data, shows a preview, and sends to your webhook.

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

### Full example

```csharp
builder.Services.AddBugSnap(options =>
{
    options.AppName = "Relivox";
    options.AppVersion = "1.8.4";
    options.Environment = "Production";
    options.MaxHttpEntries = 30;
    options.MaxJsErrors = 15;
    options.RateLimitSeconds = 60;
    options.EnableConsoleDestination = true; // dev only

    // Webhook (primary)
    options.Destinations.Add(new WebhookDestination(
        "https://your-api.com/bugs",
        headers: new Dictionary<string, string> { ["X-Api-Key"] = "secret" }
    ));

    // GitHub Issues
    options.Destinations.Add(new GitHubIssueDestination(
        "your-org/your-repo",
        "ghp_your_token",
        labels: ["bug", "auto-triage"]
    ));
});
```

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

**Payload:** Full `BugReport` object serialized as JSON (camelCase). Screenshots are base64-encoded.

### GitHubIssueDestination

Creates a GitHub Issue with structured markdown body.

```csharp
// Using "owner/repo" format
new GitHubIssueDestination("your-org/your-repo", "ghp_your_token")

// With custom labels
new GitHubIssueDestination("your-org/your-repo", "ghp_your_token",
    labels: ["bug", "auto-triage", "severity:high"])

// Using separate owner and repo
new GitHubIssueDestination("your-org", "your-repo", "ghp_your_token")
```

**Requires:** GitHub Personal Access Token with `repo` scope.

The issue body includes: severity, category, user description, environment info, correlation IDs, HTTP activity table, JS errors, and app-specific context.

### ConsoleDestination

Logs to browser console. For development/testing only.

```csharp
options.EnableConsoleDestination = true;
```

No manual instantiation needed — IJSRuntime is resolved from DI automatically.

### Custom Destination

Implement `IBugReportDestination`:

```csharp
using BugSnap.Destinations;
using BugSnap.Models;

public class SlackDestination : IBugReportDestination
{
    public string Name => "Slack";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        // POST to Slack webhook
        // ...
        return new BugReportResult(true, Name);
    }
}
```

Register it:

```csharp
options.Destinations.Add(new SlackDestination("https://hooks.slack.com/..."));
```

---

## Components

### BugReportButton

Floating action button that opens the bug report modal.

```razor
@using BugSnap.Components

@* Bottom-right (default) *@
<BugReportButton />

@* Other positions *@
<BugReportButton Position="BugSnapPosition.BottomLeft" />
<BugReportButton Position="BugSnapPosition.TopRight" />
<BugReportButton Position="BugSnapPosition.TopLeft" />

@* With label *@
<BugReportButton Label="Reportar bug" />
```

### BugReportModal

You can also use the modal directly for custom trigger buttons:

```razor
@using BugSnap.Components

<button @onclick="() => _showModal = true">Report Bug</button>

<BugReportModal @bind-Visible="_showModal" OnReportSubmitted="HandleReport" />

@code {
    private bool _showModal;

    private void HandleReport(BugReport report)
    {
        // optional: do something after successful submit
    }
}
```

### Programmatic Context Collection

Access the collector directly for custom flows:

```razor
@inject BugSnap.Services.BugContextCollector Collector

@code {
    private async Task CollectContext()
    {
        var snapshot = await Collector.CollectAsync();
        // snapshot.CurrentRoute, snapshot.RecentRequests, etc.
    }
}
```

---

## App-Specific Context

BugSnap collects generic context automatically (route, browser, HTTP history, JS errors). For domain-specific data, implement `IBugContextProvider`:

```csharp
using BugSnap.Services;

public class MyAppBugContextProvider : IBugContextProvider
{
    private readonly AuthStateProvider _auth;
    private readonly AppState _state;

    public MyAppBugContextProvider(AuthStateProvider auth, AppState state)
    {
        _auth = auth;
        _state = state;
    }

    public Task<IDictionary<string, string>> GetCustomContextAsync(CancellationToken ct = default)
    {
        var ctx = new Dictionary<string, string>
        {
            ["UserId"] = _auth.UserId?.ToString() ?? "",
            ["Role"] = _auth.Role ?? "",
            ["TenantId"] = _auth.TenantId?.ToString() ?? "",
            ["ActiveModule"] = _state.CurrentModule ?? "",
            ["SelectedItemId"] = _state.SelectedId?.ToString() ?? "",
        };
        return Task.FromResult<IDictionary<string, string>>(ctx);
    }
}
```

Register it **after** `AddBugSnap()`:

```csharp
builder.Services.AddBugSnap(options => { ... });
builder.Services.AddScoped<IBugContextProvider, MyAppBugContextProvider>();
```

The custom context appears in:
- The preview panel (under "campos de contexto do app")
- The webhook JSON payload (in `context.customContext`)
- The GitHub Issue body (under "App Context" table)

---

## Fingerprint (Deduplication)

BugSnap generates a fingerprint hash for each report, enabling backend deduplication. The fingerprint is a 16-character hex string (SHA256) based on:

- **Normalized route** — query params stripped, trailing slashes removed, GUIDs/numeric IDs replaced with `{id}`
- **Error signature** — first JS error message (truncated, normalized) OR first HTTP error (status + normalized URL)
- **App version**

Same bug, same route, same error class = same fingerprint — regardless of entity IDs in the URL.

The fingerprint is included in the payload as `fingerprint`:

```json
{
  "fingerprint": "a1b2c3d4e5f6a7b8",
  "title": "...",
  "context": { ... }
}
```

Your backend can use it for:
- Counting occurrences of the same bug
- Suppressing duplicate reports
- Tracking regression across versions

---

## Category Auto-Suggestion

BugSnap automatically suggests a bug category based on collected evidence. The suggestion pre-selects the category dropdown in the modal, but the user can always override it.

**Heuristic rules (in priority order):**

| Priority | Condition | Category |
|----------|-----------|----------|
| 1 | HTTP 401/403 in recent requests | Auth |
| 2 | HTTP 5xx in recent requests | API |
| 3 | SignalR state is Disconnected/Failed | SignalR |
| 4 | JS errors present | UI |
| 5 | HTTP 4xx in recent requests | API |
| 6 | Requests slower than 3 seconds | Performance |
| 7 | None of the above | Other |

The suggested category is also included in the context snapshot as `suggestedCategory` for backend analysis.

---

## What Gets Captured Automatically

| Data | Source | Always |
|------|--------|--------|
| Current route | `NavigationManager.Uri` | Yes |
| Browser / OS | `navigator.userAgent` via JS | Yes |
| Screen size | `window.innerWidth x innerHeight` | Yes |
| Recent HTTP requests | `HttpActivityTracker` DelegatingHandler | Yes* |
| HTTP error snippets | Response body (first 500 chars, sanitized) | On 4xx/5xx |
| Failed requests (network errors) | Exception type + message | On failure |
| TraceId | `traceparent` or `X-Trace-Id` response header | If present |
| CorrelationId | `X-Correlation-Id` or `X-Request-Id` response header | If present |
| JS errors | `window.error` + `unhandledrejection` events | Yes |
| App name / version / env | `BugSnapOptions` config | If configured |
| Custom context | `IBugContextProvider` implementation | If registered |

*\* Requires wiring `HttpActivityTracker` into your HttpClient pipeline (see Quick Start step 3).*

---

## Sanitization

BugSnap sanitizes sensitive data **before** it leaves the browser. This is not optional.

### What gets masked

| Pattern | Action |
|---------|--------|
| `Authorization: Bearer xxx` in error snippets | `Bearer [REDACTED]` |
| `Authorization: Basic xxx` in error snippets | `Basic [REDACTED]` |
| `Authorization: xxx` (non-Bearer/Basic) in error snippets | `Authorization: [REDACTED]` |
| `Cookie: xxx` / `Set-Cookie: xxx` in error snippets | `Cookie: [REDACTED]` |
| `X-Api-Key: xxx` / `X-Token: xxx` in error snippets | `X-Api-Key: [REDACTED]` |
| `?token=xxx`, `?key=xxx`, `?api_key=xxx`, `?access_token=xxx`, `?secret=xxx` in URLs | `?token=[REDACTED]` |

### What gets truncated

| Target | Limit |
|--------|-------|
| Error response snippets | `MaxErrorSnippetLength` (default: 500 chars) |
| Total payload | 1MB max (screenshot included) |

### Preview

The modal shows a human-readable preview before sending:

```
O que sera enviado:
- Tela: /inbox/conversations/abc-123
- Navegador: Chrome 120 / Windows
- 3 requisicoes recentes capturadas
- 1 erros JS detectados
- 5 campos de contexto do app
- Dados sensiveis mascarados automaticamente

[Ver detalhes tecnicos]
```

Users can expand "Ver detalhes tecnicos" to see the full sanitized payload.

---

## Payload Schema

The webhook receives a JSON payload with this structure:

```json
{
  "schemaVersion": "1.0.0",
  "sdkVersion": "",
  "title": "Mensagem nao envia",
  "description": "Clico em enviar e nada acontece",
  "severity": "High",
  "category": "API",
  "screenshotFileName": "screenshot.png",
  "screenshot": "base64...",
  "context": {
    "currentRoute": "/inbox/conversations/abc-123",
    "browserInfo": "Mozilla/5.0 ...",
    "screenSize": "1920x1080",
    "signalRState": null,
    "appName": "MyApp",
    "appVersion": "1.0.0",
    "environment": "Production",
    "collectedAtUtc": "2026-03-25T14:32:01Z",
    "traceId": "abc123",
    "correlationId": "def456",
    "sessionId": null,
    "pageInstanceId": "a1b2c3d4",
    "recentRequests": [
      {
        "method": "POST",
        "url": "https://api.example.com/api/inbox/send",
        "statusCode": 500,
        "durationMs": 234,
        "timestampUtc": "2026-03-25T14:32:01Z",
        "errorSnippet": "NullReferenceException: Object reference...",
        "traceId": "abc123",
        "correlationId": "def456"
      }
    ],
    "recentJsErrors": [
      {
        "message": "TypeError: Cannot read property 'id' of null",
        "source": "https://app.example.com/_framework/blazor.webassembly.js",
        "line": 142,
        "column": 15,
        "timestampUtc": "2026-03-25T14:31:59Z"
      }
    ],
    "customContext": {
      "TenantId": "tenant-abc",
      "UserId": "user-123",
      "ActiveConnectionId": "conn-xyz"
    }
  },
  "reportId": "a1b2c3d4e5f6",
  "createdAtUtc": "2026-03-25T14:32:05Z"
}
```

---

## CSS Customization

BugSnap uses CSS variables. Override them to match your app's theme:

```css
:root {
    --bugsnap-primary: #0F8B95;
    --bugsnap-primary-hover: #0d7a83;
    --bugsnap-bg: #ffffff;
    --bugsnap-text: #1e293b;
    --bugsnap-border: #e2e8f0;
    --bugsnap-radius: 8px;
}
```

---

## Architecture

```
Your Blazor App
    |
    +-- BugReportButton / BugReportModal
    |       |
    |       +-- BugContextCollector
    |       |       |
    |       |       +-- HttpActivityTracker (ring buffer of last 20 requests)
    |       |       +-- JsErrorCollector (JS interop: errors, browser, screen)
    |       |       +-- IBugContextProvider (your app-specific context)
    |       |
    |       +-- PayloadSanitizer (masks tokens/headers before send)
    |       |
    |       +-- MultiDestinationDispatcher
    |               |
    |               +-- WebhookDestination (POST JSON)
    |               +-- GitHubIssueDestination (creates Issue)
    |               +-- ConsoleDestination (browser console)
    |               +-- YourCustomDestination
```

---

## Requirements

- .NET 9
- Blazor WebAssembly or Blazor Server
- No external JS dependencies
- No NuGet dependencies beyond `Microsoft.AspNetCore.Components.Web`

---

## License

MIT
