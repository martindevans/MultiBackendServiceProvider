using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class MultiBackendServiceProviderTests
{
    [TestMethod]
    public async Task NoBackendsSelectsNone()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>()
        );

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNull(scope);
    }

    [TestMethod]
    public async Task SelectNoneSelectsNone()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new NoneSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)));

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNull(scope);
    }

    [TestMethod]
    public async Task AllUnhealthySelectsNone()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(false)));

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNull(scope);
    }

    [TestMethod]
    public async Task BusyBackendSelectsNext()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 0, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("c", 1, new FixedHealthChecker(true)));

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("c", scope.Backend);
    }

    [TestMethod]
    public async Task StatusReportIsCorrect()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new NoneSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 2, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("c", 3, new FixedHealthChecker(true)));

        var status = await provider.GetStatus(default);

        Assert.HasCount(3, status);

        var a = status.Single(a => a.Backend.Equals("a"));
        var b = status.Single(a => a.Backend.Equals("b"));
        var c = status.Single(a => a.Backend.Equals("c"));
        
        Assert.IsTrue(a.IsHealthy);
        Assert.AreEqual(1, a.AvailableSlots);
        Assert.AreEqual(1, a.TotalSlots);
        
        Assert.IsFalse(b.IsHealthy);
        Assert.AreEqual(2, b.AvailableSlots);
        Assert.AreEqual(2, b.TotalSlots);

        Assert.IsTrue(c.IsHealthy);
        Assert.AreEqual(3, c.AvailableSlots);
        Assert.AreEqual(3, c.TotalSlots);
    }

    [TestMethod]
    public async Task ItAppliesFiltering()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new NameFilter("b"),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("c", 1, new FixedHealthChecker(true)));

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Backend);
    }

    private class NameFilter
        : IBackendFilter<string>
    {
        private readonly string _expected;

        public NameFilter(string expected)
        {
            _expected = expected;
        }
        
        public async ValueTask<bool> Filter(string backend, IReadOnlyCollection<string> tags)
        {
            return backend.Equals(_expected);
        }
    }
}