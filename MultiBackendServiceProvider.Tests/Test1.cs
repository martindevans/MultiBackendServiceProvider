using System.Net;
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
}
