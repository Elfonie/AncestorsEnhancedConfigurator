namespace AncestorsEnhanced.Infrastructure.Editing;

/// <summary>
/// Single global async mutation gate for every write that touches the game's
/// configuration or save files. All mutating operations (settings apply/undo,
/// free camera, checkpoints, restore and cheats) must run through
/// <see cref="Run"/> so concurrent writes are serialized. This is deliberately a
/// single conservative semaphore (see RB-2 / F001); finer-grained locks can be
/// introduced later without changing call sites.
/// </summary>
internal static class MutationCoordinator
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Gate.Wait();
        try
        {
            action();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static T Run<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Gate.Wait();
        try
        {
            return action();
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await action().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}