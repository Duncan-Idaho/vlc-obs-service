namespace VlcObsService.Vlc;

public class VlcInstanceManager: IAsyncDisposable
{
    private readonly VlcService service;
    private readonly ILogger<VlcInstanceManager> logger;

    private readonly SemaphoreSlim semaphoreSlim = new(1);
    private Task<VlcInstance>? instanceTask;

    public VlcInstanceManager(VlcService service, ILogger<VlcInstanceManager> logger)
    {
        this.service = service;
        this.logger = logger;
    }

    private async Task<VlcInstance> EnsureStartedAsync()
    {
        if (instanceTask is { IsCompletedSuccessfully: true })
            return instanceTask.Result;

        await semaphoreSlim.WaitAsync();
        try
        {
            if (instanceTask is { IsCompletedSuccessfully: true })
                return instanceTask.Result;

            instanceTask = StartAsync();
        }
        finally
        {
            semaphoreSlim.Release();
        }

        logger.LogInformation("Starting VLC");
        return await instanceTask;
    }

    private async Task<VlcInstance> StartAsync()
    {
        var process = service.Start()
            ?? throw new InvalidOperationException("Failed to start the process");
        var instance = new VlcInstance(service, process);

        process.Exited += (_, _) => _ = ResetWithLock();

        await Task.Delay(500);
        return instance;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(true);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            await EnsureClosedAsync();
        }
    }

    public async Task EnsureClosedAsync()
    {
        if (instanceTask == null)
            return;

        bool canceled = false;
        try
        {
            logger.LogInformation("Starting to stop VLC before closing");
            await StopAsync();
        }
        catch (TaskCanceledException)
        {
            canceled = true;
            throw;
        }
        finally
        {
            // If we changed our mind, we don't close the process anymore
            if (!canceled)
            {
                var previousInstance = await ResetWithLock();
                if (previousInstance!= null)
                    (await previousInstance).Dispose();

                logger.LogInformation("VLC stopped");
            }
            else
            {
                logger.LogInformation("Aborted stopping VLC");
            }
        }
    }

    private async Task<Task<VlcInstance>?> ResetWithLock()
    {
        await semaphoreSlim.WaitAsync();
        try
        {
            return Interlocked.Exchange(ref instanceTask, null);
        }
        finally
        {
            semaphoreSlim.Release();
        }
    }

    public async Task PlayAsync()
        => await (await EnsureStartedAsync()).PlayAsync();

    public async Task StopAsync()
    {
        var instanceTask = this.instanceTask;
        if (instanceTask is null)
            return;

        await (await instanceTask).StopAsync();
    }
}
