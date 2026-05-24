using System.Reflection;

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
        var lightlyLoaded = new BackendState<string>("light", 4, new FixedHealthChecker(true));
        var heavilyLoaded = new BackendState<string>("heavy", 4, new FixedHealthChecker(true));
        await ReserveSlots(heavilyLoaded, 3);
        await ReserveSlots(lightlyLoaded, 1);

        var selector = new LoadFactorSelector<string>();

        var result = await selector.Select([heavilyLoaded, lightlyLoaded], CancellationToken.None);

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

    private static async Task ReserveSlots(BackendState<string> backend, int count)
    {
        var wait = typeof(BackendState<string>).GetMethod("Wait", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(wait);

        for (var i = 0; i < count; i++)
        {
            var acquired = (Task<bool>)wait.Invoke(backend, [TimeSpan.Zero, CancellationToken.None])!;
            Assert.IsTrue(await acquired);
        }
    }
}
