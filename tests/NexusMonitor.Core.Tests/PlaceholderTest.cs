using FluentAssertions;
using Microsoft.Extensions.Logging;
using NexusMonitor.Core.Tests.Helpers;
using Xunit;

namespace NexusMonitor.Core.Tests;

/// <summary>
/// Sanity tests that verify the test infrastructure itself is wired up correctly.
/// These are not testing production logic — they confirm that helpers, mocks,
/// and the DB wrapper all construct without error.
/// </summary>
public class InfrastructureSanityTests
{
    [Fact]
    public void InMemoryDatabase_CanBeCreated()
    {
        using var db = new TestMetricsDatabase();

        db.Database.Should().NotBeNull();
        db.Database.Connection.Should().NotBeNull();
        // Schema initialises meta table — schema_version row inserted as '1'
        db.Database.GetMeta("schema_version").Should().Be(1L);
    }

    [Fact]
    public async Task MockFactory_CanCreateProcessProviderMock()
    {
        var mock = MockFactory.CreateProcessProvider();

        mock.Should().NotBeNull();
        // Default setup — returns empty list without throwing
        var result = await mock.Object.GetProcessesAsync();
        result.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task MockFactory_CanCreateMetricsProviderMock()
    {
        var mock = MockFactory.CreateMetricsProvider();

        mock.Should().NotBeNull();
        var metrics = await mock.Object.GetMetricsAsync();
        metrics.Should().NotBeNull();
    }

    [Fact]
    public void MockFactory_CanCreateLoggerMock()
    {
        var mock = MockFactory.CreateLogger<InfrastructureSanityTests>();

        mock.Should().NotBeNull();
        // Loose mock — calling Log() via the interface directly should not throw
        var act = () => mock.Object.Log(
            LogLevel.Information,
            new EventId(0),
            "test message",
            null,
            (s, _) => s);
        act.Should().NotThrow();
    }

    [Fact]
    public void RxTestHelper_CanCreateTestScheduler()
    {
        var scheduler = RxTestHelper.CreateTestScheduler();
        scheduler.Should().NotBeNull();
    }
}
