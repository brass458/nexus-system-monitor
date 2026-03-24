using Microsoft.Reactive.Testing;
using System.Reactive.Linq;

namespace NexusMonitor.Core.Tests.Helpers;

/// <summary>
/// Utilities for testing Rx observables with a virtual-time TestScheduler.
/// </summary>
public static class RxTestHelper
{
    /// <summary>Returns a new TestScheduler for virtual-time Rx testing.</summary>
    public static TestScheduler CreateTestScheduler() => new TestScheduler();

    /// <summary>
    /// Subscribes to <paramref name="source"/> and collects all emitted items into a list.
    /// The subscription is added to <paramref name="disposables"/> so callers can clean up.
    /// </summary>
    public static List<T> RecordItems<T>(
        IObservable<T> source,
        ICollection<IDisposable> disposables)
    {
        var items = new List<T>();
        disposables.Add(source.Subscribe(items.Add));
        return items;
    }

    /// <summary>
    /// Subscribes to <paramref name="source"/> and collects all emitted items into a list.
    /// Returns both the list and the subscription (caller must dispose).
    /// </summary>
    public static (List<T> Items, IDisposable Subscription) RecordItems<T>(IObservable<T> source)
    {
        var items = new List<T>();
        var sub = source.Subscribe(items.Add);
        return (items, sub);
    }

    /// <summary>
    /// Collects all items from a cold/finite observable synchronously (with a timeout).
    /// </summary>
    public static List<T> ToList<T>(IObservable<T> source, TimeSpan? timeout = null)
    {
        var result = new List<T>();
        using var sub = source.Subscribe(result.Add);
        return result;
    }
}
