namespace MultiBackendServiceProvider;

/// <summary>
/// Select one backend from a group of healthy backends
/// </summary>
public interface IBackendSelector<TBackend>
{
    /// <summary>
    /// Select one backend from a group of healthy backends
    /// </summary>
    /// <param name="backends">Set of backends to choose from</param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    ValueTask<Backend<TBackend>?> Select(IReadOnlyList<Backend<TBackend>> backends, CancellationToken cancellation);
}

/// <summary>
/// Always select nothing
/// </summary>
/// <typeparam name="TBackend"></typeparam>
public class NoneSelector<TBackend>
    : IBackendSelector<TBackend>
{
    public ValueTask<Backend<TBackend>?> Select(IReadOnlyList<Backend<TBackend>> backends, CancellationToken cancellation)
    {
        return new ValueTask<Backend<TBackend>?>(result: null);
    }
}

/// <summary>
/// Select a random backend
/// </summary>
/// <typeparam name="TBackend"></typeparam>
public class RandomSelector<TBackend>
    : IBackendSelector<TBackend>
{
    public ValueTask<Backend<TBackend>?> Select(IReadOnlyList<Backend<TBackend>> backends, CancellationToken cancellation)
    {
        if (backends.Count == 0)
            return new ValueTask<Backend<TBackend>?>(result: null);

        var index = Random.Shared.Next(backends.Count);
        return new ValueTask<Backend<TBackend>?>(result: backends[index]);
    }
}

/// <summary>
/// Always select the first item
/// </summary>
/// <typeparam name="TBackend"></typeparam>
public class FirstSelector<TBackend>
    : IBackendSelector<TBackend>
{
    public ValueTask<Backend<TBackend>?> Select(IReadOnlyList<Backend<TBackend>> backends, CancellationToken cancellation)
    {
        return new ValueTask<Backend<TBackend>?>(result:
            backends.Count > 0
                ? backends[0]
                : null
        );
    }
}

/// <summary>
/// Choose the backend with the lowest load factor (percentage of total slots in use)
/// </summary>
/// <typeparam name="TBackend"></typeparam>
public class LoadFactorSelector<TBackend>
    : IBackendSelector<TBackend>
{
    public ValueTask<Backend<TBackend>?> Select(IReadOnlyList<Backend<TBackend>> backends, CancellationToken cancellation)
    {
        if (backends.Count == 0)
            return new ValueTask<Backend<TBackend>?>(result: null);

        Backend<TBackend>? selectedBackend = null;
        var lowestLoadFactor = float.MaxValue;

        foreach (var backend in backends)
        {
            cancellation.ThrowIfCancellationRequested();

            if (backend.TotalSlots <= 0)
                continue;

            var loadFactor = 1 - (float)backend.AvailableSlots / backend.TotalSlots;
            if (loadFactor < lowestLoadFactor)
            {
                lowestLoadFactor = loadFactor;
                selectedBackend = backend;
            }
        }

        return new ValueTask<Backend<TBackend>?>(result: selectedBackend);
    }
}