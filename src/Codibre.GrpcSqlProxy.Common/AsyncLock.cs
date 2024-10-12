using System.Collections.Concurrent;

namespace Codibre.GrpcSqlProxy.Common;

public sealed class AsyncLock : IDisposable
{
    private static readonly ConcurrentDictionary<object, SemaphoreSlim> _semaphores = new();
    private readonly object _key;
    private readonly SemaphoreSlim _semaphore;
    private AsyncLock(object key, SemaphoreSlim semaphore)
    {
        _key = key;
        _semaphore = semaphore;
    }

    public static async Task<AsyncLock> Lock(object lockObject)
    {
        if (!_semaphores.TryGetValue(lockObject, out var semaphore))
        {
            semaphore = new SemaphoreSlim(1, 1);
            _semaphores[lockObject] = semaphore;
        }
        await semaphore.WaitAsync();

        return new AsyncLock(lockObject, semaphore);
    }

    public void Dispose()
    {
        _semaphores.TryRemove(_key, out _);
        _semaphore.Release();
    }
}