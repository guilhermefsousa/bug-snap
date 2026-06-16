namespace BugSnap.Models;

/// <summary>
/// Memory snapshot at capture time. The <c>JsHeap*</c> fields come from the
/// browser's <c>performance.memory</c> API, which is Chromium-only — on Firefox
/// and Safari they are null because the API is not exposed (we never invent
/// values). <see cref="ManagedHeapBytes"/> comes from <c>GC.GetTotalMemory</c>
/// on the .NET side and is the cross-browser fallback signal.
/// </summary>
public class MemoryInfo
{
    public long? JsHeapUsedBytes { get; set; }
    public long? JsHeapTotalBytes { get; set; }
    public long? JsHeapLimitBytes { get; set; }
    public long? ManagedHeapBytes { get; set; }
}
