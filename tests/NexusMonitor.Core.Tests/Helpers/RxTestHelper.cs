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
    /// Collects all items from a cold/synchronous observable into a list.
    /// Only use for observables that complete synchronously (e.g., Observable.Return, Observable.Empty).
    /// For async or hot observables use <see cref="RecordItems{T}(IObservable{T}, ICollection{IDisposable})"/> instead.
    /// </summary>
    public static List<T> ToListSync<T>(IObservable<T> source)
    {
        var result = new List<T>();
        using var sub = source.Subscribe(result.Add);
        return result;
    }
}
