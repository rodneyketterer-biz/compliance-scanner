using System.Collections.Concurrent;

namespace SiteScan.Application.Crawling;

/// <summary>
/// Represents a thread-safe FIFO frontier used to hold <c>FrontierItem</c> instances for crawling.
/// </summary>
/// <remarks>
/// Internally uses <c>ConcurrentQueue&lt;FrontierItem&gt;</c> to allow concurrent producers and consumers.
/// All members are safe for concurrent use. Consumers should treat <see cref="IsEmpty"/> as a snapshot;
/// the queue may be modified by other threads immediately after the property is read.
/// </remarks>

/// <summary>
/// Enqueues the specified <c>FrontierItem</c> for later processing by the crawler.
/// </summary>
/// <param name="item">The <c>FrontierItem</c> to add to the frontier.</param>

/// <summary>
/// Attempts to dequeue the next available <c>FrontierItem</c>.
/// </summary>
/// <param name="item">
/// When this method returns, contains the dequeued <c>FrontierItem</c> if the method returns <c>true</c>;
/// otherwise, the default value for <c>FrontierItem</c>.
/// </param>
/// <returns><c>true</c> if an item was dequeued; otherwise, <c>false</c>.</returns>

/// <summary>
/// Gets a value indicating whether the frontier currently contains no items.
/// </summary>
/// <remarks>
/// This property returns a snapshot of the queue's emptiness at the time of the call and may become stale
/// immediately after returning due to concurrent operations.
/// </remarks>
internal sealed class CrawlFrontier
{
    private readonly ConcurrentQueue<FrontierItem> _queue = new();

    public void Enqueue(FrontierItem item) => _queue.Enqueue(item);

    public bool TryDequeue(out FrontierItem item) => _queue.TryDequeue(out item);

    public bool IsEmpty => _queue.IsEmpty;
}

internal sealed record FrontierItem(Uri CanonicalUrl, int Depth);
