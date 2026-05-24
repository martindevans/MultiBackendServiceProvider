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
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)));

        using var scope = await provider.GetBackend(CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Backend);
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
    : IBackendHealthChecker
{
    public ValueTask<bool> CheckHealth(HttpClient client, ILogger logger, CancellationToken cancellation)
    {
        return ValueTask.FromResult(healthy);
    }
}
