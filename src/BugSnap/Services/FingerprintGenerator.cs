using System.Text.RegularExpressions;
using BugSnap.Models;

namespace BugSnap.Services;

public static class FingerprintGenerator
{
    // Matches GUIDs and numeric IDs in URL paths
    private static readonly Regex _idPattern = new(
        @"/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\d{2,})/",
        RegexOptions.Compiled);

    public static string Generate(BugContextSnapshot context)
    {
        var route = NormalizePath(context.CurrentRoute);
        var errorSignature = GetErrorSignature(context);
        var version = context.AppVersion ?? "unknown";

        var raw = $"{route}|{errorSignature}|{version}";

        return ComputeShortHash(raw);
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
