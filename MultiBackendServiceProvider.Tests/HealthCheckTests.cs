using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public sealed class HealthCheckTests
{
    [TestMethod]
    public async Task CustomHealthChecker_IsUsedPerBackend()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)));

        var request = new BackendRequest<string>();
        using var scope = await request.Acquire(provider, CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Backend.Value);
    }

    [TestMethod]
    public async Task FailingBackendIsNotReturned()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FailLaterHealthChecker(1)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)));

        var request = new BackendRequest<string>();
        using var scope = await request.Acquire(provider, CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Backend.Value);
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

public sealed class FixedHealthChecker
    : IBackendHealthChecker
{
    public bool Healthy { get; set; }

    public FixedHealthChecker(bool healthy)
    {
        Healthy = healthy;
    }
    
    public ValueTask<bool> CheckHealth(CancellationToken cancellation)
    {
        return ValueTask.FromResult(Healthy);
    }
}

public sealed class FailLaterHealthChecker
    : IBackendHealthChecker
{
    private int _countdown;

    public FailLaterHealthChecker(int countdown)
    {
        _countdown = countdown;
    }

    public ValueTask<bool> CheckHealth(CancellationToken cancellation)
    {
        if (_countdown <= 0)
            return ValueTask.FromResult(false);

        _countdown--;
        return ValueTask.FromResult(true);
    }
}