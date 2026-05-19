using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public sealed class HealthCheckTests
{
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
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler)
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        return new HttpClient(handler, disposeHandler: false);
    }
}

public sealed class AlwaysHealthyHandler
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

public sealed class FixedHealthChecker(bool healthy)
    : IEndpointHealthChecker
{
    public ValueTask<bool> CheckHealth(HttpClient client, ILogger logger, CancellationToken cancellation)
    {
        return ValueTask.FromResult(healthy);
    }
}
