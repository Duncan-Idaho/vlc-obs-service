namespace VlcObsService.Vlc;

public class VlcInstanceManager: IAsyncDisposable
{
    private readonly VlcService service;
    private Task<VlcInstance>? instanceTask;

    public VlcInstanceManager(VlcService service)
    {
        this.service = service;
    }

    public Task<VlcInstance> EnsureStartedAsync()
    {
        if (instanceTask == null || instanceTask.IsFaulted || instanceTask.IsCanceled)
            instanceTask = StartAsync();
        return instanceTask;
    }

    private async Task<VlcInstance> StartAsync()
    {
        var process = (await service.Start())
            ?? throw new InvalidOperationException("Failed to start the process");
        return new VlcInstance(service, process);
    }

    public async Task EnsureClosedAsync()
    {
        try
        {
            await StopIfPossibleAsync();
        }
        finally
        {
            var oldInstanceTask = Interlocked.Exchange(ref instanceTask, null);
            if (oldInstanceTask != null)
                (await oldInstanceTask).Dispose();
        }
    }

    public async Task StopIfPossibleAsync()
    {
        var instanceTask = this.instanceTask;
        if (instanceTask is null)
            return;

        await (await instanceTask).StopAsync();
    }

    public async Task PlayAsync()
        => await (await EnsureStartedAsync()).PlayAsync();

    public async Task StopAsync() 
        => await (await EnsureStartedAsync()).StopAsync();

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
}
