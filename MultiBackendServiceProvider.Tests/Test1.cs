using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public sealed class Test1
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

        var tasks = Enumerable.Range(0, 128).Select(_ => Task.Run(() => scope.Dispose()));
        await Task.WhenAll(tasks);

        var status = await provider.GetStatus();
        Assert.AreEqual(1, status[0].AvailableSlots);
        Assert.AreEqual(1, status[0].MaxSlots);
    }

    [TestMethod]
    public async Task CustomHealthChecker_IsUsedPerBackend()
    {
        var provider = new MultiBackendServiceProvider<string>(
            new StubHttpClientFactory(new AlwaysHealthyHandler()),
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new MultiBackendServiceProvider<string>.EndpointConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.EndpointConfig("b", 1, new FixedHealthChecker(true)));

        using var scope = await provider.GetEndpoint(CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Endpoint);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class AlwaysHealthyHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FixedHealthChecker(bool healthy)
        : MultiBackendServiceProvider<string>.IHealthChecker
    {
        public Task<bool> CheckHealth(HttpClient client, ILogger logger, CancellationToken cancellation)
        {
            return Task.FromResult(healthy);
        }
    }
}
