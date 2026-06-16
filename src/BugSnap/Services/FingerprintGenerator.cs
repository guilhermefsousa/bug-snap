using System.Text.RegularExpressions;
using BugSnap.Models;

namespace BugSnap.Services;

public static class FingerprintGenerator
{
    // Matches GUIDs and numeric IDs in URL paths
    private static readonly Regex _idPattern = new(
        @"/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\d{2,})/",
        RegexOptions.Compiled);

    // Matches GUIDs and standalone numeric runs anywhere in a free-text description
    private static readonly Regex _descriptionIdPattern = new(
        @"\b([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\d+)\b",
        RegexOptions.Compiled);

    private static readonly Regex _whitespacePattern = new(@"\s+", RegexOptions.Compiled);

    public static string Generate(BugContextSnapshot context)
        => Generate(context, userDescription: null);

    /// <summary>
    /// Computes a deduplication fingerprint. When there is no technical error signal
    /// (no JS error and no HTTP &gt;= 400 — typical of a thin manual report), the user's
    /// description is folded into the hash so distinct manual reports on the same route
    /// don't collapse into one fingerprint. When a technical signature exists, the
    /// description is ignored to keep crash dedup stable across the auto-capture path.
    /// </summary>
    public static string Generate(BugContextSnapshot context, string? userDescription)
    {
        var route = NormalizePath(context.CurrentRoute);
        var errorSignature = GetErrorSignature(context);
        var version = context.AppVersion ?? "unknown";

        var raw = $"{route}|{errorSignature}|{version}";

        // Thin manual report (no technical signal): discriminate by description so
        // two different manual reports on the same route get different fingerprints.
        if (errorSignature == "no-error" && !string.IsNullOrWhiteSpace(userDescription))
        {
            raw += $"|desc:{NormalizeDescription(userDescription)}";
        }

        return ComputeShortHash(raw);
    }

    private static string NormalizeDescription(string description)
    {
        // Lowercase, strip GUIDs/numbers, collapse whitespace, truncate ~80 chars
        var normalized = _descriptionIdPattern.Replace(description, "{id}");
        normalized = _whitespacePattern.Replace(normalized, " ").Trim().ToLowerInvariant();
        if (normalized.Length > 80)
            normalized = normalized[..80];
        return normalized;
    }

    private static string NormalizePath(string path)
    {
        // Strip query string
        var idx = path.IndexOf('?');
        if (idx >= 0) path = path[..idx];
        // Strip trailing slash, lowercase
        path = path.TrimEnd('/').ToLowerInvariant();
        // Replace GUIDs and numeric IDs with placeholder
        path = _idPattern.Replace(path, "/{id}/");
        return path.TrimEnd('/');
    }

    private static string GetErrorSignature(BugContextSnapshot context)
    {
        // Priority: JS errors first, then HTTP errors
        var firstJsError = context.RecentJsErrors.FirstOrDefault();
        if (firstJsError is not null)
        {
            // Truncate and normalize — strip dynamic content (GUIDs, numbers, quoted strings)
            var msg = NormalizeErrorMessage(firstJsError.Message);
            return $"js:{msg}";
        }

        var firstHttpError = context.RecentRequests
            .FirstOrDefault(r => r.StatusCode >= 400);
        if (firstHttpError is not null)
        {
            // Normalize the URL (strip query, replace IDs)
            var normalizedUrl = NormalizePath(firstHttpError.Url);
            return $"http:{firstHttpError.StatusCode}:{normalizedUrl}";
        }

        return "no-error";
    }

    private static string NormalizeErrorMessage(string message)
    {
        if (message.Length > 80)
            message = message[..80];

        // Replace GUIDs
        message = _idPattern.Replace(message, "/{id}/");
        return message.ToLowerInvariant();
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
