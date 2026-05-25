namespace MultiBackendServiceProvider.Tests;

[TestClass]
public class ScopeTests
{
    [TestMethod]
    public async Task ScopeDispose_IsThreadSafe_AndOnlyReleasesOnce()
    {
        var state = new BackendState<string>("a", 1, new FixedHealthChecker(true));
        var scope = await state.Acquire(TimeSpan.FromMilliseconds(1), TestContext.CancellationToken);
        
        //var scope = await provider.GetBackend(CancellationToken.None);
        Assert.IsNotNull(scope);

        var tasks = Enumerable.Range(0, 128).Select(_ => Task.Run(scope.Dispose, TestContext.CancellationToken));
        await Task.WhenAll(tasks);

        Assert.AreEqual(1, state.AvailableSlots);
        Assert.AreEqual(1, state.TotalSlots);
    }

    [TestMethod]
    public async Task ScopeFinalize_Disposes()
    {
        var state = new BackendState<string>("a", 1, new FixedHealthChecker(true));

        // Take a slot
        await AcquireAndLose(state);
        
        // Force a full GC
        GC.Collect(int.MaxValue, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();

        // Check that the slot has been freed up
        Assert.AreEqual(1, state.AvailableSlots);
        Assert.AreEqual(1, state.TotalSlots);
    }

    private async Task AcquireAndLose(BackendState<string> state)
    {
        await state.Acquire(TimeSpan.FromMilliseconds(1), TestContext.CancellationToken);
    }

    [TestMethod]
    public async Task ScopeAcquire_OnlyAcquiresUpToMax()
    {
        var state = new BackendState<string>("a", 1, new FixedHealthChecker(true));
        
        var scope1 = await state.Acquire(TimeSpan.FromMilliseconds(1), TestContext.CancellationToken);
        var scope2 = await state.Acquire(TimeSpan.FromMilliseconds(1), TestContext.CancellationToken);
        
        Assert.IsNotNull(scope1);
        Assert.IsNull(scope2);
    }

    public TestContext TestContext { get; set; } = null!;
}