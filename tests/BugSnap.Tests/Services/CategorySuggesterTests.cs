using BugSnap.Models;
using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class CategorySuggesterTests
{
    // --- Auth ---

    [Fact]
    public void Suggest_When401Present_ShouldReturnAuth()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 401, Url = "/api/me" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Auth, category);
    }

    [Fact]
    public void Suggest_When403Present_ShouldReturnAuth()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 403, Url = "/api/admin" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Auth, category);
    }

    // --- API (5xx) ---

    [Fact]
    public void Suggest_When500Present_ShouldReturnAPI()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 500, Url = "/api/data" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.API, category);
    }

    [Fact]
    public void Suggest_When502Present_ShouldReturnAPI()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 502, Url = "/api/gateway" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.API, category);
    }

    // --- API (4xx other than 401/403) ---

    [Fact]
    public void Suggest_When404Present_ShouldReturnAPI()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 404, Url = "/api/resource" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.API, category);
    }

    // --- SignalR ---

    [Fact]
    public void Suggest_WhenSignalRDisconnected_ShouldReturnSignalR()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            SignalRState = "Disconnected",
            RecentRequests = [],
            RecentJsErrors = []
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.SignalR, category);
    }

    // --- UI ---

    [Fact]
    public void Suggest_WhenJsErrorsPresentWithNoHttpErrors_ShouldReturnUI()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [],
            RecentJsErrors = [new JsErrorEntry { Message = "TypeError: null is not an object" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.UI, category);
    }

    // --- Performance ---

    [Fact]
    public void Suggest_WhenSlowRequestAbove3000msWithNoErrors_ShouldReturnPerformance()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 200, Url = "/api/report", DurationMs = 3500 }],
            RecentJsErrors = []
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Performance, category);
    }

    // --- Other ---

    [Fact]
    public void Suggest_WhenNoErrorsNoJsErrorsAndSignalRConnected_ShouldReturnOther()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            SignalRState = "Connected",
            RecentRequests = [],
            RecentJsErrors = []
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Other, category);
    }

    // --- Priority: Auth beats API (401 + 500) ---

    [Fact]
    public void Suggest_WhenBoth401And500Present_ShouldReturnAuth()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests =
            [
                new HttpActivityEntry { StatusCode = 500, Url = "/api/data" },
                new HttpActivityEntry { StatusCode = 401, Url = "/api/me" }
            ]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Auth, category);
    }

    // --- Priority: API beats UI (500 + JS errors) ---

    [Fact]
    public void Suggest_WhenBoth500AndJsErrorsPresent_ShouldReturnAPI()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [new HttpActivityEntry { StatusCode = 500, Url = "/api/orders" }],
            RecentJsErrors = [new JsErrorEntry { Message = "ReferenceError: x is not defined" }]
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.API, category);
    }

    // --- Empty requests and errors → Other ---

    [Fact]
    public void Suggest_WhenRequestsAndJsErrorsAreEmpty_ShouldReturnOther()
    {
        // Arrange
        var context = new BugContextSnapshot
        {
            RecentRequests = [],
            RecentJsErrors = [],
            SignalRState = null
        };

        // Act
        var category = CategorySuggester.Suggest(context);

        // Assert
        Assert.Equal(BugSnapCategory.Other, category);
    }
}
