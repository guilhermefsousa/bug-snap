using BugSnap.Services;

namespace BugSnap.Tests.Services;

public class RingBufferTests
{
    // --- Add within capacity ---

    [Fact]
    public void Add_WhenWithinCapacity_ShouldReturnAllItemsInInsertionOrder()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Assert
        var result = buffer.ToList();
        Assert.Equal(new[] { 1, 2, 3 }, result);
    }

    // --- Overflow: oldest dropped ---

    [Fact]
    public void Add_WhenBeyondCapacity_ShouldDropOldestAndKeepNewest()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4); // overflows: 1 is dropped
        buffer.Add(5); // overflows: 2 is dropped

        // Assert
        var result = buffer.ToList();
        Assert.Equal(new[] { 3, 4, 5 }, result);
    }

    [Fact]
    public void Add_WhenExactlyAtCapacity_ShouldRetainAllItems()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act
        buffer.Add(10);
        buffer.Add(20);
        buffer.Add(30);

        // Assert
        var result = buffer.ToList();
        Assert.Equal(new[] { 10, 20, 30 }, result);
    }

    // --- Count ---

    [Fact]
    public void Count_WhenAddingItemsWithinCapacity_ShouldMatchAddedCount()
    {
        // Arrange
        var buffer = new RingBuffer<int>(10);

        // Act & Assert
        Assert.Equal(0, buffer.Count);
        buffer.Add(1);
        Assert.Equal(1, buffer.Count);
        buffer.Add(2);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void Count_WhenAddingBeyondCapacity_ShouldNotExceedCapacity()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);

        // Act
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);
        buffer.Add(5);

        // Assert
        Assert.Equal(3, buffer.Count);
    }

    // --- Clear ---

    [Fact]
    public void Clear_WhenCalled_ShouldEmptyTheBuffer()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);

        // Act
        buffer.Clear();

        // Assert
        Assert.Equal(0, buffer.Count);
        Assert.Empty(buffer.ToList());
    }

    [Fact]
    public void Clear_WhenFollowedByAdds_ShouldAcceptNewItemsCorrectly()
    {
        // Arrange
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);

        // Act
        buffer.Clear();
        buffer.Add(99);

        // Assert
        Assert.Equal(1, buffer.Count);
        Assert.Equal(new[] { 99 }, buffer.ToList());
    }

    // --- ToList snapshot ---

    [Fact]
    public void ToList_WhenModifyingReturnedList_ShouldNotAffectBuffer()
    {
        // Arrange
        var buffer = new RingBuffer<int>(5);
        buffer.Add(1);
        buffer.Add(2);

        // Act
        var snapshot = buffer.ToList();
        snapshot.Add(999); // mutate the returned list

        // Assert — buffer still has 2 items, not 3
        Assert.Equal(2, buffer.Count);
        Assert.Equal(2, buffer.ToList().Count);
    }

    [Fact]
    public void ToList_WhenBufferEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var buffer = new RingBuffer<string>(5);

        // Act
        var result = buffer.ToList();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // --- Thread safety ---

    [Fact]
    public void Add_WhenCalledConcurrently_ShouldNotThrow()
    {
        // Arrange
        var buffer = new RingBuffer<int>(50);

        // Act & Assert — must not throw or corrupt state
        Parallel.For(0, 200, i => buffer.Add(i));

        Assert.Equal(50, buffer.Count);
        Assert.Equal(50, buffer.ToList().Count);
    }
}
