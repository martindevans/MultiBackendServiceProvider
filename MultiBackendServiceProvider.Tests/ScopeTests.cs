using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class ScopeTests
{
    [TestMethod]
    public async Task ScopeDispose_IsThreadSafe_AndOnlyReleasesOnce()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new MultiBackendServiceProvider<string>.EndpointConfig("a", 1, new Uri("http://health.local/")));

        var scope = await provider.GetEndpoint(CancellationToken.None);
        Assert.IsNotNull(scope);

        var tasks = Enumerable.Range(0, 128).Select(_ => Task.Run(scope.Dispose, TestContext.CancellationToken));
        await Task.WhenAll(tasks);

        var status = await provider.GetStatus(TestContext.CancellationToken);
        Assert.AreEqual(1, status[0].AvailableSlots);
        Assert.AreEqual(1, status[0].MaxSlots);
    }

    public TestContext TestContext { get; set; }
}