using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BugSnap.Models;

namespace BugSnap.Destinations;

public class GitHubIssueDestination : IBugReportDestination
{
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _token;
    private readonly List<string> _labels;

    public GitHubIssueDestination(string owner, string repo, string token, IEnumerable<string>? labels = null)
    {
        _owner = owner;
        _repo = repo;
        _token = token;
        _labels = labels?.ToList() ?? ["bug"];
    }

    /// <summary>
    /// Convenience constructor: "owner/repo" format.
    /// </summary>
    public GitHubIssueDestination(string ownerSlashRepo, string token, IEnumerable<string>? labels = null)
    {
        var parts = ownerSlashRepo.Split('/');
        if (parts.Length != 2)
            throw new ArgumentException("Expected 'owner/repo' format.", nameof(ownerSlashRepo));

        _owner = parts[0];
        _repo = parts[1];
        _token = token;
        _labels = labels?.ToList() ?? ["bug"];
    }

    public string Name => "GitHub";

    public async Task<BugReportResult> SubmitAsync(BugReport report, CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BugSnap/1.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(15);

            var title = $"[BugSnap] {report.Title}";
            var body = FormatMarkdown(report);

            var payload = new
            {
                title,
                body,
                labels = _labels
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var url = $"https://api.github.com/repos/{_owner}/{_repo}/issues";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var issueUrl = ExtractIssueUrl(responseBody);

            return new BugReportResult(true, Name, Url: issueUrl);
        }
        catch (Exception ex)
        {
            return new BugReportResult(false, Name, Error: ex.Message);
        }
    }

    private static string FormatMarkdown(BugReport report)
    {
        var sb = new StringBuilder();
        var ctx = report.Context;
        var severityEmoji = report.Severity switch
        {
            BugSnapSeverity.Critical => "🔴",
            BugSnapSeverity.High => "🟠",
            BugSnapSeverity.Medium => "🟡",
            BugSnapSeverity.Low => "🟢",
            _ => "⚪"
        };

        sb.AppendLine("## Bug Report");
        sb.AppendLine();
        sb.AppendLine($"**Severity:** {severityEmoji} {report.Severity}");
        sb.AppendLine($"**Category:** {report.Category}");

        if (!string.IsNullOrEmpty(ctx.AppName))
        {
            var appInfo = ctx.AppName;
            if (!string.IsNullOrEmpty(ctx.AppVersion)) appInfo += $" v{ctx.AppVersion}";
            if (!string.IsNullOrEmpty(ctx.Environment)) appInfo += $" ({ctx.Environment})";
            sb.AppendLine($"**App:** {appInfo}");
        }

        sb.AppendLine($"**Reported at:** {report.CreatedAtUtc:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine();

        // User description
        sb.AppendLine("### Description");
        sb.AppendLine(report.Description);
        sb.AppendLine();

        // Environment
        sb.AppendLine("### Environment");
        sb.AppendLine("| Key | Value |");
        sb.AppendLine("|-----|-------|");
        sb.AppendLine($"| Route | `{ctx.CurrentRoute}` |");
        if (!string.IsNullOrEmpty(ctx.BrowserInfo))
            sb.AppendLine($"| Browser | {TruncateBrowser(ctx.BrowserInfo)} |");
        if (!string.IsNullOrEmpty(ctx.ScreenSize))
            sb.AppendLine($"| Screen | {ctx.ScreenSize} |");
        if (!string.IsNullOrEmpty(ctx.SignalRState))
            sb.AppendLine($"| SignalR | {ctx.SignalRState} |");
        if (!string.IsNullOrEmpty(ctx.TraceId))
            sb.AppendLine($"| TraceId | `{ctx.TraceId}` |");
        if (!string.IsNullOrEmpty(ctx.CorrelationId))
            sb.AppendLine($"| CorrelationId | `{ctx.CorrelationId}` |");
        sb.AppendLine();

        // Custom context
        if (ctx.CustomContext.Count > 0)
        {
            sb.AppendLine("### App Context");
            sb.AppendLine("| Key | Value |");
            sb.AppendLine("|-----|-------|");
            foreach (var (key, value) in ctx.CustomContext)
                sb.AppendLine($"| {key} | {value} |");
            sb.AppendLine();
        }

        // HTTP activity
        if (ctx.RecentRequests.Count > 0)
        {
            sb.AppendLine("### Recent HTTP Activity");
            sb.AppendLine("| Time | Method | URL | Status | Duration |");
            sb.AppendLine("|------|--------|-----|--------|----------|");
            foreach (var req in ctx.RecentRequests)
            {
                var time = req.TimestampUtc.ToString("HH:mm:ss");
                sb.AppendLine($"| {time} | {req.Method} | `{req.Url}` | {req.StatusCode} | {req.DurationMs}ms |");
            }
            sb.AppendLine();
        }

        // JS errors
        if (ctx.RecentJsErrors.Count > 0)
        {
            sb.AppendLine("### JS Errors");
            foreach (var err in ctx.RecentJsErrors)
            {
                var location = err.Source is not null ? $" at {err.Source}" : "";
                if (err.Line.HasValue) location += $":{err.Line}";
                sb.AppendLine($"- `{err.Message}`{location}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine("*Generated by [BugSnap](https://github.com/guilhermefsousa/bug-snap)*");

        return sb.ToString();
    }

    private static string TruncateBrowser(string userAgent)
    {
        // Extract just the browser name from the full UA string
        if (userAgent.Length <= 80) return userAgent;
        return userAgent[..80] + "...";
    }

    private static string? ExtractIssueUrl(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("html_url", out var urlProp))
                return urlProp.GetString();
        }
        catch
        {
            // ignore parse errors
        }
        return null;
    }
}
