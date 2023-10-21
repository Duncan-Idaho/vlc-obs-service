namespace VlcObsService.Vlc;

public class VlcInstanceManager: IAsyncDisposable
{
    private readonly VlcService service;
    private Task<VlcInstance>? instance;

    public VlcInstanceManager(VlcService service)
    {
        this.service = service;
    }

    public Task<VlcInstance> EnsureStartedAsync()
    {
        if (instance == null || instance.IsFaulted || instance.IsCanceled)
            instance = StartAsync();
        return instance;
    }

    private async Task<VlcInstance> StartAsync()
    {
        var process = (await service.Start())
            ?? throw new InvalidOperationException("Failed to start the process");
        return new VlcInstance(service, process);
    }

    public async Task EnsureClosedAsync()
    {
        var oldInstanceTask = Interlocked.Exchange(ref instance, null);
        if (oldInstanceTask == null)
            return;

        (await oldInstanceTask).Dispose();
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
