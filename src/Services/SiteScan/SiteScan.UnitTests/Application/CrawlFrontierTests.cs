using NUnit.Framework;
using SiteScan.Application.Crawling;
using System;

namespace SiteScan.UnitTests.Application;

[TestFixture]
public class CrawlFrontierTests
{
    [Test]
    public void Enqueue_Then_Dequeue_Preserves_Fifo_Order_And_IsEmpty()
    {
        var frontier = new CrawlFrontier();

        Assert.That(frontier.IsEmpty, Is.True);

        var a = new FrontierItem(new Uri("https://example.com/a"), 0);
        var b = new FrontierItem(new Uri("https://example.com/b"), 1);
        var c = new FrontierItem(new Uri("https://example.com/c"), 2);

        frontier.Enqueue(a);
        frontier.Enqueue(b);
        frontier.Enqueue(c);

        Assert.That(frontier.IsEmpty, Is.False);

        Assert.That(frontier.TryDequeue(out var first), Is.True);
        Assert.That(first, Is.EqualTo(a));

        Assert.That(frontier.TryDequeue(out var second), Is.True);
        Assert.That(second, Is.EqualTo(b));

        Assert.That(frontier.TryDequeue(out var third), Is.True);
        Assert.That(third, Is.EqualTo(c));

        Assert.That(frontier.IsEmpty, Is.True);
        Assert.That(frontier.TryDequeue(out _), Is.False);
    }
}
