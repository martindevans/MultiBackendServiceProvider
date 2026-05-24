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
        var heavilyLoaded = new BackendState<string>("heavy", 4, new FixedHealthChecker(true));
        var lightlyLoaded = new BackendState<string>("light", 4, new FixedHealthChecker(true));
        using var heavy1 = await heavilyLoaded.Acquire(TimeSpan.Zero, CancellationToken.None);
        using var heavy2 = await heavilyLoaded.Acquire(TimeSpan.Zero, CancellationToken.None);
        using var heavy3 = await heavilyLoaded.Acquire(TimeSpan.Zero, CancellationToken.None);
        using var light1 = await lightlyLoaded.Acquire(TimeSpan.Zero, CancellationToken.None);
        var selector = new LoadFactorSelector<string>();
        var result = await selector.Select([heavilyLoaded, lightlyLoaded], CancellationToken.None);

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
        var unavailable = new BackendState<string>("unavailable", 0, new FixedHealthChecker(true));
        var available = new BackendState<string>("available", 1, new FixedHealthChecker(true));
        var selector = new LoadFactorSelector<string>();
        var result = await selector.Select([unavailable, available], CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("available", result.Backend);
    }
}