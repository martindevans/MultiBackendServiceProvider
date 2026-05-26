using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class BackendRequestTests
{
    [TestMethod]
    public async Task FailingBackendIsNotReturned()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(false)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true)))
        ;

        var request = new BackendRequest<string>();
        using var scope = await request.Acquire(provider, CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("b", scope.Backend.Value);
    }

    [TestMethod]
    public async Task RequestIsSticky()
    {
        var checker = new FixedHealthChecker(false);

        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, checker),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true))
        );

        var request = new BackendRequest<string>();

        string req1;
        using (var scope = await request.Acquire(provider, CancellationToken.None))
            req1 = scope!.Backend.Value;

        string req2;
        using (var scope = await request.Acquire(provider, CancellationToken.None))
            req2 = scope!.Backend.Value;

        checker.Healthy = true;
        
        string req3;
        using (var scope = await request.Acquire(provider, CancellationToken.None))
            req3 = scope!.Backend.Value;

        Assert.AreEqual(req1, req2);
        Assert.AreEqual(req2, req3);
    }

    [TestMethod]
    public async Task RequestChangesDueToHealthCheckFail()
    {
        var checker1 = new FixedHealthChecker(false);
        var checker2 = new FixedHealthChecker(true);

        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, checker1),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, checker2)
        );

        var request = new BackendRequest<string>();

        // Get a backend
        using (var scope = await request.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("b", scope?.Backend.Value);

        checker2.Healthy = false;
        checker1.Healthy = true;

        // Get another backend. Won't be sticky, since that backend has failed.

        using (var scope = await request.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);
    }

    [TestMethod]
    public async Task RequestChangesDueToBusy()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true))
        );

        var request1 = new BackendRequest<string>();
        var request2 = new BackendRequest<string>();

        // Get a backend for request 1
        using (var scope = await request1.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);

        // Get a backend for request 2
        using (var scope = await request2.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);

        // Both backends are now sticky to backend A!

        // Acquire a scope for request 1
        using (var scope = await request1.Acquire(provider, CancellationToken.None))
        {
            Assert.AreEqual("a", scope?.Backend.Value);

            // Get a backend for request 2. Since A is busy this will be B
            using (var scopeInner = await request2.Acquire(provider, CancellationToken.None))
                Assert.AreEqual("b", scopeInner?.Backend.Value);
        }
    }

    [TestMethod]
    public async Task CannotAcquireScopeWhenAllBackendsAreBusy()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("a", 1, new FixedHealthChecker(true)),
            new MultiBackendServiceProvider<string>.BackendConfig("b", 1, new FixedHealthChecker(true))
        );

        var request1 = new BackendRequest<string>();
        var request2 = new BackendRequest<string>();
        var request3 = new BackendRequest<string>();

        // Get a backend for request 1
        using (var scope = await request1.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);

        // Get a backend for request 2
        using (var scope = await request2.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);

        // Get a backend for request 3
        using (var scope = await request3.Acquire(provider, CancellationToken.None))
            Assert.AreEqual("a", scope?.Backend.Value);

        // All backends are now sticky to backend A!

        // Acquire a scope for request 1
        using (var scope1 = await request1.Acquire(provider, CancellationToken.None))
        {
            Assert.AreEqual("a", scope1?.Backend.Value);

            // Get a backend for request 2. Since A is busy this will be B
            using (var scope2 = await request2.Acquire(provider, CancellationToken.None))
            {
                Assert.AreEqual("b", scope2?.Backend.Value);

                // All backends are busy! Return null.
                using (var scope3 = await request2.Acquire(provider, CancellationToken.None))
                {
                    Assert.IsNull(scope3);
                }
            }
        }
    }
}