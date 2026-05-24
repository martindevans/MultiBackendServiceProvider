using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class SelectorTests
{
    [TestMethod]
    public async Task RandomSelector_ReturnsNull_WhenNoBackends()
    {
        var selector = new RandomSelector<string>();

        var result = await selector.Select([], CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task RandomSelector_ReturnsOneOfProvidedBackends()
    {
        var backends = new[]
        {
            new BackendState<string>("a", 1, new FixedHealthChecker(true)),
            new BackendState<string>("b", 1, new FixedHealthChecker(true)),
            new BackendState<string>("c", 1, new FixedHealthChecker(true)),
        };
        var selector = new RandomSelector<string>();

        var result = await selector.Select(backends, CancellationToken.None);

        Assert.IsNotNull(result);
        CollectionAssert.Contains(backends.Select(b => b.Backend).ToArray(), result.Backend);
    }

    [TestMethod]
    public async Task FirstSelector_ReturnsNull_WhenNoBackends()
    {
        var selector = new FirstSelector<string>();

        var result = await selector.Select([], CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task FirstSelector_ReturnsFirstBackend()
    {
        var backends = new[]
        {
            new BackendState<string>("a", 1, new FixedHealthChecker(true)),
            new BackendState<string>("b", 1, new FixedHealthChecker(true)),
        };
        var selector = new FirstSelector<string>();

        var result = await selector.Select(backends, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("a", result.Backend);
    }

    [TestMethod]
    public async Task LoadFactorSelector_ReturnsNull_WhenNoBackends()
    {
        var selector = new LoadFactorSelector<string>();

        var result = await selector.Select([], CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task LoadFactorSelector_SelectsBackendWithLowestLoadFactor()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new BackendNameFilter(),
            new LoadFactorSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("heavy", 4, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("light", 4, new FixedHealthChecker(true)));

        using var heavy1 = await provider.GetBackend(["heavy"], CancellationToken.None);
        using var heavy2 = await provider.GetBackend(["heavy"], CancellationToken.None);
        using var heavy3 = await provider.GetBackend(["heavy"], CancellationToken.None);
        using var light1 = await provider.GetBackend(["light"], CancellationToken.None);
        using var result = await provider.GetBackend(CancellationToken.None);

        Assert.IsNotNull(heavy1);
        Assert.IsNotNull(heavy2);
        Assert.IsNotNull(heavy3);
        Assert.IsNotNull(light1);
        Assert.IsNotNull(result);
        Assert.AreEqual("light", result.Backend);
    }

    [TestMethod]
    public async Task LoadFactorSelector_IgnoresBackendsWithoutCapacity()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new BackendNameFilter(),
            new LoadFactorSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("unavailable", 0, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("available", 1, new FixedHealthChecker(true)));

        using var result = await provider.GetBackend(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("available", result.Backend);
    }

    private sealed class BackendNameFilter
        : IBackendFilter<string>
    {
        public ValueTask<bool> Filter(string backend, IReadOnlyCollection<string> tags)
        {
            return ValueTask.FromResult(tags.Count == 0 || tags.Contains(backend));
        }
    }
}
