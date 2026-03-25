using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class DefaultBugContextProviderTests
{
    [Fact]
    public async Task GetCustomContextAsync_WhenCalled_ShouldReturnEmptyDictionary()
    {
        // Arrange
        var provider = new DefaultBugContextProvider();

        // Act
        var result = await provider.GetCustomContextAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCustomContextAsync_WhenCalledMultipleTimes_ShouldAlwaysReturnEmptyDictionary()
    {
        // Arrange
        var provider = new DefaultBugContextProvider();

        // Act
        var first = await provider.GetCustomContextAsync();
        var second = await provider.GetCustomContextAsync();

        // Assert
        Assert.Empty(first);
        Assert.Empty(second);
    }
}
