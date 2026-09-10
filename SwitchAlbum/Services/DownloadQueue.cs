namespace SwitchAlbum.Services;

/// <summary>限流下载队列（MTP 单设备串行，2 并发为较稳妥的取值）。</summary>
public sealed class DownloadQueue
{
    private readonly SemaphoreSlim _semaphore;

    public DownloadQueue(int maxConcurrent = 2) => _semaphore = new SemaphoreSlim(maxConcurrent);

    public async Task EnqueueAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await operation(ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
