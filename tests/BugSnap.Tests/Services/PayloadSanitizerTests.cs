using BugSnap;
using BugSnap.Models;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class PayloadSanitizerTests
{
    private static BugSnapOptions DefaultOptions(int maxSnippetLength = 500)
        => new() { MaxErrorSnippetLength = maxSnippetLength };

    private static BugReport ReportWith(params HttpActivityEntry[] entries)
    {
        var report = new BugReport();
        report.Context.RecentRequests = entries.ToList();
        return report;
    }

    private static HttpActivityEntry EntryWithUrl(string url, string? errorSnippet = null)
        => new() { Url = url, ErrorSnippet = errorSnippet };

    // --- Query param masking ---

    [Fact]
    public void Sanitize_WhenUrlContainsTokenParam_ShouldRedactTokenValue()
    {
        // Arrange
        var entry = EntryWithUrl("https://api.example.com/data?token=supersecret");
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("token=[REDACTED]", entry.Url);
        Assert.DoesNotContain("supersecret", entry.Url);
    }

    [Fact]
    public void Sanitize_WhenUrlContainsApiKeyParam_ShouldRedactValue()
    {
        // Arrange
        var entry = EntryWithUrl("https://api.example.com/data?api_key=mykey123");
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("api_key=[REDACTED]", entry.Url);
        Assert.DoesNotContain("mykey123", entry.Url);
    }

    [Fact]
    public void Sanitize_WhenUrlContainsAccessTokenParam_ShouldRedactValue()
    {
        // Arrange
        var entry = EntryWithUrl("https://api.example.com/oauth?access_token=oauth_xyz");
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("access_token=[REDACTED]", entry.Url);
        Assert.DoesNotContain("oauth_xyz", entry.Url);
    }

    [Fact]
    public void Sanitize_WhenUrlContainsMultipleSensitiveParams_ShouldRedactAll()
    {
        // Arrange
        var entry = EntryWithUrl("https://api.example.com/data?token=abc&api_key=def&page=2");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("token=[REDACTED]", entry.Url);
        Assert.Contains("api_key=[REDACTED]", entry.Url);
        Assert.Contains("page=2", entry.Url); // safe param unchanged
        Assert.Equal(2, result.QueryParamsMasked);
    }

    [Fact]
    public void Sanitize_WhenUrlContainsOnlyNonSensitiveParams_ShouldLeaveUrlUnchanged()
    {
        // Arrange
        const string originalUrl = "https://api.example.com/search?page=1&size=10&sort=asc";
        var entry = EntryWithUrl(originalUrl);
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Equal(originalUrl, entry.Url);
    }

    [Fact]
    public void Sanitize_WhenUrlHasNoQueryString_ShouldLeaveUrlUnchanged()
    {
        // Arrange
        const string originalUrl = "https://api.example.com/users/42";
        var entry = EntryWithUrl(originalUrl);
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Equal(originalUrl, entry.Url);
    }

    // --- ErrorSnippet: Bearer/Basic masking ---

    [Fact]
    public void Sanitize_WhenErrorSnippetContainsBearerToken_ShouldMaskIt()
    {
        // Arrange
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig");
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("Bearer [REDACTED]", entry.ErrorSnippet);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", entry.ErrorSnippet);
    }

    [Fact]
    public void Sanitize_WhenErrorSnippetContainsBasicToken_ShouldMaskIt()
    {
        // Arrange
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: "Authorization: Basic dXNlcjpwYXNz");
        var report = ReportWith(entry);

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("Basic [REDACTED]", entry.ErrorSnippet);
        Assert.DoesNotContain("dXNlcjpwYXNz", entry.ErrorSnippet);
    }

    // --- ErrorSnippet truncation ---

    [Fact]
    public void Sanitize_WhenErrorSnippetExceedsMaxLength_ShouldTruncate()
    {
        // Arrange
        var longSnippet = new string('a', 600);
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: longSnippet);
        var report = ReportWith(entry);
        var options = DefaultOptions(maxSnippetLength: 100);

        // Act
        var result = PayloadSanitizer.Sanitize(report, options);

        // Assert
        Assert.Equal(100, entry.ErrorSnippet!.Length);
        Assert.Equal(1, result.SnippetsTruncated);
    }

    [Fact]
    public void Sanitize_WhenErrorSnippetIsWithinMaxLength_ShouldNotTruncate()
    {
        // Arrange
        const string snippet = "Short error message";
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: snippet);
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions(maxSnippetLength: 500));

        // Assert
        Assert.Equal(snippet, entry.ErrorSnippet);
        Assert.Equal(0, result.SnippetsTruncated);
    }

    // --- SanitizationResult counts ---

    [Fact]
    public void Sanitize_WhenMultipleEntriesHaveSensitiveData_ShouldReturnCorrectCounts()
    {
        // Arrange
        var entry1 = EntryWithUrl("https://example.com?token=abc", errorSnippet: new string('z', 600));
        var entry2 = EntryWithUrl("https://example.com?api_key=def", errorSnippet: null);
        var report = ReportWith(entry1, entry2);
        var options = DefaultOptions(maxSnippetLength: 50);

        // Act
        var result = PayloadSanitizer.Sanitize(report, options);

        // Assert
        Assert.Equal(2, result.QueryParamsMasked);
        Assert.Equal(1, result.SnippetsTruncated);
        Assert.Equal(0, result.HeaderPatternsMasked);
    }

    [Fact]
    public void Sanitize_WhenNoSensitiveData_ShouldReturnZeroCounts()
    {
        // Arrange
        var entry = EntryWithUrl("https://example.com/api/items?page=1", errorSnippet: "minor error");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Equal(0, result.QueryParamsMasked);
        Assert.Equal(0, result.SnippetsTruncated);
        Assert.Equal(0, result.HeaderPatternsMasked);
    }

    // --- JS error sanitization ---

    [Fact]
    public void Sanitize_WhenStackTraceContainsBearer_ShouldRedact()
    {
        // Arrange
        var report = new BugReport();
        var jsError = new JsErrorEntry
        {
            Message = "auth error",
            StackTrace = "Error at fetch: headers: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def\n  at api.js:42"
        };
        report.Context.RecentJsErrors = [jsError];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("[REDACTED]", jsError.StackTrace);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9.abc.def", jsError.StackTrace);
    }

    [Fact]
    public void Sanitize_WhenJsErrorMessageContainsEmail_ShouldRedact()
    {
        // Arrange
        var report = new BugReport();
        var jsError = new JsErrorEntry
        {
            Message = "Failed to notify user@example.com about error"
        };
        report.Context.RecentJsErrors = [jsError];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("[REDACTED_EMAIL]", jsError.Message);
        Assert.DoesNotContain("user@example.com", jsError.Message);
    }

    [Fact]
    public void Sanitize_WhenJsErrorSourceContainsToken_ShouldRedact()
    {
        // Arrange
        var report = new BugReport
        {
            Context = new BugContextSnapshot
            {
                RecentJsErrors = new List<JsErrorEntry>
                {
                    new() { Source = "https://app.com/Bearer abc123def456 callback" }
                }
            }
        };
        var options = new BugSnapOptions { MaxErrorSnippetLength = 500 };

        // Act
        PayloadSanitizer.Sanitize(report, options);

        // Assert
        Assert.Contains("[REDACTED]", report.Context.RecentJsErrors[0].Source);
        Assert.DoesNotContain("abc123def456", report.Context.RecentJsErrors[0].Source);
    }

    [Fact]
    public void Sanitize_WhenStackTraceExceedsMaxLength_ShouldTruncateAfterRedaction()
    {
        // Arrange
        const int maxLength = 50;
        var options = DefaultOptions(maxSnippetLength: maxLength);
        var report = new BugReport();
        var jsError = new JsErrorEntry
        {
            // Short safe prefix + long safe suffix — redaction won't change length,
            // truncation should kick in
            StackTrace = "Error: safe message\n" + new string('x', 200)
        };
        report.Context.RecentJsErrors = [jsError];

        // Act
        PayloadSanitizer.Sanitize(report, options);

        // Assert
        Assert.Equal(maxLength, jsError.StackTrace!.Length);
    }
}
