using BugSnap.Models;

namespace BugSnap.Services;

public sealed class HttpActivityBuffer
{
    private readonly RingBuffer<HttpActivityEntry> _buffer;

    public HttpActivityBuffer(int capacity = 20)
    {
        _buffer = new RingBuffer<HttpActivityEntry>(capacity);
    }

    public void Add(HttpActivityEntry entry) => _buffer.Add(entry);

    public IReadOnlyList<HttpActivityEntry> GetRecentActivity() => _buffer.ToList();
}
