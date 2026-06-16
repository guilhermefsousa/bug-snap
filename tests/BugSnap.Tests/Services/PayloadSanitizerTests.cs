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
    public void Sanitize_WhenUrlContainsOnlySafeAllowlistParams_ShouldLeaveUrlUnchanged()
    {
        // Arrange — all keys are in the safe allowlist
        const string originalUrl = "https://api.example.com/search?page=1&page_size=10&sort=asc";
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

    // --- Redact-by-default: PII query params are masked (Rule 7) ---

    [Theory]
    [InlineData("email", "user@example.com")]
    [InlineData("phone", "5511999998888")]
    [InlineData("cpf", "12345678900")]
    [InlineData("q", "Joao da Silva")]
    public void Sanitize_WhenUrlContainsPiiQueryParam_ShouldRedactValue(string key, string value)
    {
        // Arrange — none of these keys are in the safe allowlist
        var entry = EntryWithUrl($"https://api.example.com/data?{key}={Uri.EscapeDataString(value)}");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains($"{key}=[REDACTED]", entry.Url);
        Assert.DoesNotContain(Uri.EscapeDataString(value), entry.Url);
        Assert.DoesNotContain("Silva", entry.Url);
        Assert.Equal(1, result.QueryParamsMasked);
    }

    [Fact]
    public void Sanitize_WhenUrlHasBareQueryToken_ShouldRedactIt()
    {
        // A flag-style token without '=' has no key → cannot be allowlisted → must be masked.
        var entry = EntryWithUrl("https://api.example.com/x?5511999998888");
        var report = ReportWith(entry);

        PayloadSanitizer.Sanitize(report, DefaultOptions());

        Assert.Contains("[REDACTED]", entry.Url);
        Assert.DoesNotContain("5511999998888", entry.Url);
    }

    // --- Rule 5: user-provided free-text fields (StepsToReproduce/ExpectedOrImpact) are sanitized ---

    [Fact]
    public void Sanitize_WhenUserTextFieldsContainPii_ShouldRedactEmailAndPhone()
    {
        // Arrange — manual-report fields with PII typed by the user
        var report = new BugReport
        {
            StepsToReproduce = "Liguei pro cliente joao@example.com no 5511988887777 e a tela quebrou",
            ExpectedOrImpact = "Esperava enviar a mensagem pro 5521977776666"
        };

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert — email + phones redacted in both fields (Rule 5)
        Assert.DoesNotContain("joao@example.com", report.StepsToReproduce);
        Assert.DoesNotContain("5511988887777", report.StepsToReproduce);
        Assert.Contains("[REDACTED_EMAIL]", report.StepsToReproduce);
        Assert.Contains("[REDACTED_PHONE]", report.StepsToReproduce);
        Assert.DoesNotContain("5521977776666", report.ExpectedOrImpact);
        Assert.Contains("[REDACTED_PHONE]", report.ExpectedOrImpact);
    }

    [Theory]
    [InlineData("page", "2")]
    [InlineData("tab", "conv")]
    [InlineData("id", "123")]
    public void Sanitize_WhenUrlContainsSafeAllowlistParam_ShouldPreserveValue(string key, string value)
    {
        // Arrange — safe navigation/pagination keys are preserved
        var entry = EntryWithUrl($"https://api.example.com/data?{key}={value}");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains($"{key}={value}", entry.Url);
        Assert.DoesNotContain("[REDACTED]", entry.Url);
        Assert.Equal(0, result.QueryParamsMasked);
    }

    [Fact]
    public void Sanitize_WhenSafeKeyDiffersOnlyByCase_ShouldStillPreserve()
    {
        // Arrange — allowlist match is case-insensitive
        var entry = EntryWithUrl("https://api.example.com/data?Page=3&TAB=inbox");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Equal("https://api.example.com/data?Page=3&TAB=inbox", entry.Url);
        Assert.Equal(0, result.QueryParamsMasked);
    }

    [Fact]
    public void Sanitize_WhenUrlMixesSafeAndUnknownParams_ShouldRedactOnlyUnknown()
    {
        // Arrange — page kept, email + name masked
        var entry = EntryWithUrl("https://api.example.com/list?page=1&email=a@b.com&name=Maria");
        var report = ReportWith(entry);

        // Act
        var result = PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("page=1", entry.Url);
        Assert.Contains("email=[REDACTED]", entry.Url);
        Assert.Contains("name=[REDACTED]", entry.Url);
        Assert.DoesNotContain("a@b.com", entry.Url);
        Assert.DoesNotContain("Maria", entry.Url);
        Assert.Equal(2, result.QueryParamsMasked);
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
    public void Sanitize_WhenErrorSnippetExceedsHttpBodyLength_ShouldTruncate()
    {
        // Arrange — HTTP error snippets are capped by MaxHttpErrorBodyLength, NOT
        // MaxErrorSnippetLength. The snippet is well above the HTTP cap here.
        var longSnippet = new string('a', 600);
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: longSnippet);
        var report = ReportWith(entry);
        var options = new BugSnapOptions { MaxHttpErrorBodyLength = 100, MaxErrorSnippetLength = 50 };

        // Act
        var result = PayloadSanitizer.Sanitize(report, options);

        // Assert — truncated to the HTTP cap (100), not the smaller MaxErrorSnippetLength (50)
        Assert.Equal(100, entry.ErrorSnippet!.Length);
        Assert.Equal(1, result.SnippetsTruncated);
    }

    [Fact]
    public void Sanitize_WhenSnippetBetweenSnippetLengthAndHttpBodyLength_ShouldNotTruncate()
    {
        // Regression guard for the B2 fix: a 1000-char HTTP error body exceeds the
        // 500-char MaxErrorSnippetLength but stays within the 2000-char MaxHttpErrorBodyLength,
        // so it must survive to the payload intact (the "bigger snippet" the README promises).
        var snippet = new string('b', 1000);
        var entry = EntryWithUrl("https://example.com/api", errorSnippet: snippet);
        var report = ReportWith(entry);

        // Act — defaults: MaxErrorSnippetLength=500, MaxHttpErrorBodyLength=2000
        var result = PayloadSanitizer.Sanitize(report, new BugSnapOptions());

        // Assert — NOT truncated; full 1000 chars preserved
        Assert.Equal(1000, entry.ErrorSnippet!.Length);
        Assert.Equal(0, result.SnippetsTruncated);
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
        // HTTP error snippets truncate on MaxHttpErrorBodyLength; 600 > 50 triggers truncation.
        var options = new BugSnapOptions { MaxHttpErrorBodyLength = 50 };

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

    // --- Console error sanitization ---

    [Fact]
    public void Sanitize_WhenConsoleErrorMessageContainsEmail_ShouldRedact()
    {
        // Arrange
        var report = new BugReport();
        var consoleError = new ConsoleErrorEntry
        {
            Message = "render failed for admin@corp.io while loading"
        };
        report.Context.RecentConsoleErrors = [consoleError];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("[REDACTED_EMAIL]", consoleError.Message);
        Assert.DoesNotContain("admin@corp.io", consoleError.Message);
    }

    [Fact]
    public void Sanitize_WhenConsoleErrorStackContainsBearer_ShouldRedact()
    {
        // Arrange
        var report = new BugReport();
        var consoleError = new ConsoleErrorEntry
        {
            Message = "fetch error",
            Stack = "at fetch headers: Bearer eyJhbGciOiJIUzI1NiJ9.secret.sig\n at app.js:12"
        };
        report.Context.RecentConsoleErrors = [consoleError];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("Bearer [REDACTED]", consoleError.Stack);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9.secret.sig", consoleError.Stack);
    }

    [Fact]
    public void Sanitize_WhenConsoleErrorMessageExceedsMaxLength_ShouldTruncate()
    {
        // Arrange
        var report = new BugReport();
        var consoleError = new ConsoleErrorEntry
        {
            Message = "safe " + new string('y', 200)
        };
        report.Context.RecentConsoleErrors = [consoleError];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions(maxSnippetLength: 40));

        // Assert
        Assert.Equal(40, consoleError.Message.Length);
    }

    // --- Breadcrumb sanitization ---

    [Fact]
    public void Sanitize_WhenBreadcrumbDetailContainsPhone_ShouldRedact()
    {
        // Arrange
        var report = new BugReport();
        var breadcrumb = new BreadcrumbEntry
        {
            Type = "navigation",
            Detail = "/contacts/5511999998888/profile"
        };
        report.Context.Breadcrumbs = [breadcrumb];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Contains("[REDACTED_PHONE]", breadcrumb.Detail);
        Assert.DoesNotContain("5511999998888", breadcrumb.Detail);
    }

    [Fact]
    public void Sanitize_WhenBreadcrumbDetailIsSafeRoute_ShouldLeaveUnchanged()
    {
        // Arrange
        var report = new BugReport();
        var breadcrumb = new BreadcrumbEntry
        {
            Type = "click",
            Detail = "open-settings"
        };
        report.Context.Breadcrumbs = [breadcrumb];

        // Act
        PayloadSanitizer.Sanitize(report, DefaultOptions());

        // Assert
        Assert.Equal("open-settings", breadcrumb.Detail);
    }

    [Fact]
    public void Sanitize_WhenBreadcrumbDetailIsNull_ShouldNotThrow()
    {
        // Arrange
        var report = new BugReport();
        var breadcrumb = new BreadcrumbEntry { Type = "navigation", Detail = null };
        report.Context.Breadcrumbs = [breadcrumb];

        // Act
        var ex = Record.Exception(() => PayloadSanitizer.Sanitize(report, DefaultOptions()));

        // Assert
        Assert.Null(ex);
        Assert.Null(breadcrumb.Detail);
    }
}
