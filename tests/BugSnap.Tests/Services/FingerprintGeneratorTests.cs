using BugSnap.Models;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class FingerprintGeneratorTests
{
    // --- Deterministic: same context, same fingerprint ---

    [Fact]
    public void Generate_WhenSameContextCalledTwice_ShouldReturnSameFingerprint()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            CurrentRoute = "/dashboard",
            AppVersion = "1.0.0",
            RecentJsErrors = [],
            RecentRequests = []
        };

        // Act
        var first = FingerprintGenerator.Generate(context);
        var second = FingerprintGenerator.Generate(context);

        // Assert
        Assert.Equal(first, second);
    }

    // --- Different routes → different fingerprints ---

    [Fact]
    public void Generate_WhenRouteDiffers_ShouldReturnDifferentFingerprint()
    {
        // Arrange
        var contextA = new BugContextSnapshot { CurrentRoute = "/home", AppVersion = "1.0.0" };
        var contextB = new BugContextSnapshot { CurrentRoute = "/settings", AppVersion = "1.0.0" };

        // Act
        var fpA = FingerprintGenerator.Generate(contextA);
        var fpB = FingerprintGenerator.Generate(contextB);

        // Assert
        Assert.NotEqual(fpA, fpB);
    }

    // --- Different errors → different fingerprints ---

    [Fact]
    public void Generate_WhenJsErrorMessageDiffers_ShouldReturnDifferentFingerprint()
    {
        // Arrange
        var contextA = new BugContextSnapshot
        {
            CurrentRoute = "/page",
            AppVersion = "1.0.0",
            RecentJsErrors = [new JsErrorEntry { Message = "TypeError: cannot read property" }]
        };
        var contextB = new BugContextSnapshot
        {
            CurrentRoute = "/page",
            AppVersion = "1.0.0",
            RecentJsErrors = [new JsErrorEntry { Message = "ReferenceError: foo is not defined" }]
        };

        // Act
        var fpA = FingerprintGenerator.Generate(contextA);
        var fpB = FingerprintGenerator.Generate(contextB);

        // Assert
        Assert.NotEqual(fpA, fpB);
    }

    // --- Different app versions → different fingerprints ---

    [Fact]
    public void Generate_WhenAppVersionDiffers_ShouldReturnDifferentFingerprint()
    {
        // Arrange
        var contextA = new BugContextSnapshot { CurrentRoute = "/page", AppVersion = "1.0.0" };
        var contextB = new BugContextSnapshot { CurrentRoute = "/page", AppVersion = "2.0.0" };

        // Act
        var fpA = FingerprintGenerator.Generate(contextA);
        var fpB = FingerprintGenerator.Generate(contextB);

        // Assert
        Assert.NotEqual(fpA, fpB);
    }

    // --- Query string is stripped: same fingerprint as clean route ---

    [Fact]
    public void Generate_WhenRouteHasQueryString_ShouldMatchFingerprintOfCleanRoute()
    {
        // Arrange
        var contextClean = new BugContextSnapshot { CurrentRoute = "/search", AppVersion = "1.0.0" };
        var contextWithQuery = new BugContextSnapshot { CurrentRoute = "/search?q=hello&page=2", AppVersion = "1.0.0" };

        // Act
        var fpClean = FingerprintGenerator.Generate(contextClean);
        var fpWithQuery = FingerprintGenerator.Generate(contextWithQuery);

        // Assert
        Assert.Equal(fpClean, fpWithQuery);
    }

    // --- Trailing slash is normalized ---

    [Fact]
    public void Generate_WhenRouteHasTrailingSlash_ShouldMatchFingerprintOfRouteWithoutSlash()
    {
        // Arrange
        var contextNoSlash = new BugContextSnapshot { CurrentRoute = "/about", AppVersion = "1.0.0" };
        var contextWithSlash = new BugContextSnapshot { CurrentRoute = "/about/", AppVersion = "1.0.0" };

        // Act
        var fpNoSlash = FingerprintGenerator.Generate(contextNoSlash);
        var fpWithSlash = FingerprintGenerator.Generate(contextWithSlash);

        // Assert
        Assert.Equal(fpNoSlash, fpWithSlash);
    }

    // --- Fingerprint is exactly 16 hex characters ---

    [Fact]
    public void Generate_Always_ShouldReturnSixteenHexCharacterString()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            CurrentRoute = "/some/path",
            AppVersion = "3.1.0",
            RecentRequests = [new HttpActivityEntry { StatusCode = 500, Url = "/api/data" }]
        };

        // Act
        var fingerprint = FingerprintGenerator.Generate(context);

        // Assert
        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
    }

    // --- Empty/null context fields don't crash ---

    [Fact]
    public void Generate_WhenAppVersionIsNull_ShouldNotThrow()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            CurrentRoute = "/page",
            AppVersion = null
        };

        // Act
        var fingerprint = FingerprintGenerator.Generate(context);

        // Assert — just must not throw and return a 16-char hex string
        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
    }

    [Fact]
    public void Generate_WhenCurrentRouteIsEmpty_ShouldNotThrow()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            CurrentRoute = "",
            AppVersion = "1.0.0"
        };

        // Act
        var fingerprint = FingerprintGenerator.Generate(context);

        // Assert
        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
    }

    // --- Context with no errors produces valid fingerprint ---

    [Fact]
    public void Generate_WhenNoErrorsPresent_ShouldReturnValidFingerprint()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            CurrentRoute = "/home",
            AppVersion = "1.0.0",
            RecentJsErrors = [],
            RecentRequests = []
        };

        // Act
        var fingerprint = FingerprintGenerator.Generate(context);

        // Assert
        Assert.Equal(16, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{16}$", fingerprint);
    }

    // --- HTTP error contributes to fingerprint (no JS errors present) ---

    [Fact]
    public void Generate_WhenHttpErrorDiffers_ShouldReturnDifferentFingerprint()
    {
        // Arrange — same route/version, different HTTP error URL
        var contextA = new BugContextSnapshot
        {
            CurrentRoute = "/page",
            AppVersion = "1.0.0",
            RecentRequests = [new HttpActivityEntry { StatusCode = 500, Url = "/api/users" }]
        };
        var contextB = new BugContextSnapshot
        {
            CurrentRoute = "/page",
            AppVersion = "1.0.0",
            RecentRequests = [new HttpActivityEntry { StatusCode = 500, Url = "/api/orders" }]
        };

        // Act
        var fpA = FingerprintGenerator.Generate(contextA);
        var fpB = FingerprintGenerator.Generate(contextB);

        // Assert
        Assert.NotEqual(fpA, fpB);
    }
}
