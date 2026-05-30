using Microsoft.Extensions.Logging.Abstractions;

namespace MultiBackendServiceProvider.Tests;

[TestClass]
public sealed class BackendDisposalTests
{
    [TestMethod]
    public async Task AddedBackend_IsSelectableAtRuntime()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>());

        provider.Add(new MultiBackendServiceProvider<string>.BackendConfig("added", 1, new FixedHealthChecker(true)));

        var request = new BackendRequest<string>();
        using var scope = await request.Acquire(provider, CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("added", scope.Backend.Value);
    }

    [TestMethod]
    public async Task RemovedBackend_IsNotSelectedOrReportedInStatus()
    {
        var provider = new MultiBackendServiceProvider<string>(
            NullLogger.Instance,
            new AcceptFilter<string>(),
            new FirstSelector<string>(),
            new MultiBackendServiceProvider<string>.BackendConfig("keep", 1, new FixedHealthChecker(true)));

        var removed = provider.Add(new MultiBackendServiceProvider<string>.BackendConfig("remove", 1, new FixedHealthChecker(true)));

        Assert.IsTrue(provider.Remove(removed));

        var request = new BackendRequest<string>();
        using var scope = await request.Acquire(provider, CancellationToken.None);
        var status = await provider.GetStatus(CancellationToken.None);

        Assert.IsNotNull(scope);
        Assert.AreEqual("keep", scope.Backend.Value);
        Assert.AreEqual(1, status.Length);
        Assert.AreEqual("keep", status[0].Backend);
    }

    [TestMethod]
    public async Task BackendDisposeAsync_WaitsForInFlightScope_AndBlocksNewAcquires()
    {
        var backend = new Backend<string>("a", 1, new FixedHealthChecker(true));
        var scope = await backend.Acquire(TimeSpan.FromMilliseconds(5), CancellationToken.None);

        Assert.IsNotNull(scope);

        var disposing = backend.DisposeAsync().AsTask();
        await Task.Delay(25);

        Assert.IsFalse(disposing.IsCompleted);
        Assert.IsFalse(backend.IsEnabled);
        Assert.IsNull(await backend.Acquire(TimeSpan.FromMilliseconds(5), CancellationToken.None));

        scope.Dispose();
        await disposing;
    }

    [TestMethod]
    public async Task BackendDisposeAsync_ForwardsToIAsyncDisposableValue()
    {
        var value = new AsyncDisposableBackendValue();
        var backend = new Backend<AsyncDisposableBackendValue>(value, 1, new FixedHealthChecker(true));

        await backend.DisposeAsync();

        Assert.AreEqual(1, value.DisposeCount);
    }

    [TestMethod]
    public async Task BackendDisposeAsync_ForwardsToIDisposableValue()
    {
        var value = new DisposableBackendValue();
        var backend = new Backend<DisposableBackendValue>(value, 1, new FixedHealthChecker(true));

        await backend.DisposeAsync();

        Assert.AreEqual(1, value.DisposeCount);
    }

    [TestMethod]
    public async Task ProviderDisposeAsync_IsIdempotent()
    {
        var value = new AsyncDisposableBackendValue();
        var provider = new MultiBackendServiceProvider<AsyncDisposableBackendValue>(
            NullLogger.Instance,
            new AcceptFilter<AsyncDisposableBackendValue>(),
            new FirstSelector<AsyncDisposableBackendValue>(),
            new MultiBackendServiceProvider<AsyncDisposableBackendValue>.BackendConfig(value, 1, new FixedHealthChecker(true)));

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        Assert.AreEqual(1, value.DisposeCount);
    }

    private sealed class AsyncDisposableBackendValue : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableBackendValue : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
