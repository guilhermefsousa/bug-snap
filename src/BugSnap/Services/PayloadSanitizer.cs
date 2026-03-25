using System.Text.RegularExpressions;
using BugSnap.Models;

namespace BugSnap.Services;

public static class PayloadSanitizer
{
    private static readonly string[] _sensitiveQueryParams =
        ["token", "key", "api_key", "access_token", "secret"];

    private static readonly Regex _bearerRegex =
        new(@"Bearer\s+\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _basicRegex =
        new(@"Basic\s+\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Mask header-like patterns that may appear in error response bodies
    // Skips values already handled by Bearer/Basic pass (negative lookahead for Bearer/Basic/[REDACTED])
    private static readonly Regex _authorizationHeaderRegex =
        new(@"Authorization[""']?\s*[:=]\s*(?!Bearer\b)(?!Basic\b)(?!\[REDACTED\])\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _cookieHeaderRegex =
        new(@"(?:Set-)?Cookie[""']?\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _apiKeyHeaderRegex =
        new(@"(?:X-Api-Key|X-Token)[""']?\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SanitizationResult Sanitize(BugReport report, BugSnapOptions options)
    {
        int headerPatternsMasked = 0;
        int queryParamsMasked = 0;
        int snippetsTruncated = 0;

        foreach (var entry in report.Context.RecentRequests)
        {
            queryParamsMasked += MaskQueryParams(entry);
            var (patternsMasked, truncated) = MaskAndTruncateSnippet(entry, options.MaxErrorSnippetLength);
            headerPatternsMasked += patternsMasked;
            snippetsTruncated += truncated;
        }

        return new SanitizationResult(headerPatternsMasked, queryParamsMasked, snippetsTruncated);
    }

    private static int MaskQueryParams(HttpActivityEntry entry)
    {
        if (string.IsNullOrEmpty(entry.Url) || !entry.Url.Contains('?'))
            return 0;

        try
        {
            int masked = 0;
            var uriString = entry.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? entry.Url
                : "https://placeholder" + entry.Url;

            var uriBuilder = new UriBuilder(uriString);
            var query = uriBuilder.Query.TrimStart('?');
            if (string.IsNullOrEmpty(query)) return 0;

            var pairs = query.Split('&');
            var modified = new List<string>(pairs.Length);

            foreach (var pair in pairs)
            {
                var eqIndex = pair.IndexOf('=');
                if (eqIndex < 0)
                {
                    modified.Add(pair);
                    continue;
                }

                var paramName = pair[..eqIndex];
                if (_sensitiveQueryParams.Contains(paramName, StringComparer.OrdinalIgnoreCase))
                {
                    modified.Add(paramName + "=[REDACTED]");
                    masked++;
                }
                else
                {
                    modified.Add(pair);
                }
            }

            if (masked > 0)
            {
                uriBuilder.Query = string.Join("&", modified);
                var rebuilt = uriBuilder.Uri.PathAndQuery;
                entry.Url = entry.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? uriBuilder.Uri.AbsoluteUri
                    : rebuilt;
            }

            return masked;
        }
        catch (FormatException)
        {
            // Malformed URL — skip sanitization rather than crash the entire dispatch
            return 0;
        }
    }

    private static (int PatternsMasked, int Truncated) MaskAndTruncateSnippet(
        HttpActivityEntry entry, int maxLength)
    {
        if (string.IsNullOrEmpty(entry.ErrorSnippet)) return (0, 0);

        int patternsMasked = 0;
        int truncated = 0;
        var snippet = entry.ErrorSnippet;

        // Mask auth tokens
        snippet = ReplaceAndCount(_bearerRegex, snippet, "Bearer [REDACTED]", ref patternsMasked);
        snippet = ReplaceAndCount(_basicRegex, snippet, "Basic [REDACTED]", ref patternsMasked);

        // Mask header-like patterns in error bodies
        snippet = ReplaceAndCount(_authorizationHeaderRegex, snippet, "Authorization: [REDACTED]", ref patternsMasked);
        snippet = ReplaceAndCount(_cookieHeaderRegex, snippet, "Cookie: [REDACTED]", ref patternsMasked);
        snippet = ReplaceAndCount(_apiKeyHeaderRegex, snippet, "X-Api-Key: [REDACTED]", ref patternsMasked);

        if (snippet.Length > maxLength)
        {
            snippet = snippet[..maxLength];
            truncated = 1;
        }

        entry.ErrorSnippet = snippet;
        return (patternsMasked, truncated);
    }

    private static string ReplaceAndCount(Regex regex, string input, string replacement, ref int count)
    {
        var matches = regex.Matches(input);
        if (matches.Count > 0)
        {
            count += matches.Count;
            return regex.Replace(input, replacement);
        }
        return input;
    }
}

public record SanitizationResult(int HeaderPatternsMasked, int QueryParamsMasked, int SnippetsTruncated);
