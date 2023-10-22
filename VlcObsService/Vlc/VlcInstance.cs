using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Diagnostics;
using VlcObsService.Vlc.Models;

namespace VlcObsService.Vlc;

public class VlcInstance : IDisposable
{
    private readonly VlcService service;
    private readonly Process process;
    private bool closeWhenDisposed = true;

    private CancellationTokenSource changesCts = new();

    public VlcInstance(VlcService service, Process process)
    {
        this.service = service;
        this.process = process;

        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            // If exited from the app, skip the close step
            closeWhenDisposed = false;
            Dispose();
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Preferable to cancel all actions before hand to ensure no requests are sent
            // if the application closes its server before sending Exited event
            changesCts.Cancel();
            if (closeWhenDisposed)
                process.CloseMainWindow();
            process.Dispose();
        }
    }

    public async Task PlayAsync(List<string> playlist)
    {
        CancellationTokenSource cts = new();
        Interlocked.Exchange(ref changesCts, cts).Cancel();

        var status = await service.GetStatus(cts.Token);
        if (status == null)
            return;

        if (status.Random == true)
            status = await service.ToggleRandom(cts.Token);

        if (status.Repeat == false)
            status = await service.ToggleRepeat(cts.Token);

        if (status.State == "paused" || status.State == "stopped")
            status = await service.SetVolume(0, cts.Token);

        await EnqueuePlaylistAsync(playlist, cts.Token);

        if (status.State == "paused")
            status = await service.Next(cts.Token);

        if (status.State == "stopped")
            status = await service.Play(cts.Token);

        await FadeTo(status, 256, 256, 2000, cts.Token);
    }

    public async Task EnqueuePlaylistAsync(List<string> requestedPlaylist, CancellationToken token)
    {
        var playlist = await service.GetPlaylist(token);
        if (playlist?.EnqueuedItems?.Length > 0)
            return;

        var urisToAdd = await GetElements(requestedPlaylist, token);

        foreach (var uri in urisToAdd)
            await service.Enqueue(uri, token);

        await service.Randomize(token);
    }

    public async Task<IEnumerable<string>> GetElements(List<string> requestedPlaylist, CancellationToken token)
    {
        var filter = service.GetFilter();

        if (requestedPlaylist.Count == 0)
            return (await service.Browse(token)).Element.GetFileUris(filter);

        var itemsUri = requestedPlaylist.Select(ToUri).ToList();

        var validFiles = itemsUri.WhereUriMatches(filter);
        var browseResults = await Task.WhenAll(itemsUri
            .Except(validFiles)
            .Select(item => service.Browse(item)));

        return validFiles
            .Concat(browseResults.SelectMany(result => result.Element.GetFileUris(filter)));

        static string ToUri(string item)
        {
            if (Uri.IsWellFormedUriString(item, UriKind.Absolute))
                return item;

            return "file:///" + string.Join('/',
                item.Split('/').Select(Uri.EscapeDataString));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource cts = new();
        Interlocked.Exchange(ref changesCts, cts).Cancel();

        var status = await service.GetStatus(cts.Token);
        if (status == null)
            return;

        if (status.State == "paused" || status.State == "stopped")
            return;

        await FadeTo(status, 0, 256, 2000, cts.Token);
        await service.Next(cts.Token);
        await service.Stop(cts.Token);
    }

    private async Task<Status> FadeTo(Status status, int targetVolume, int amplitude, int duration, CancellationToken token)
    {
        var interval = duration / amplitude;

        var originalVolume = status.Volume;
        var delta = targetVolume - originalVolume;
        var actualDuration = interval * Math.Abs(delta);

        var stopWatch = Stopwatch.StartNew();

        while (stopWatch.ElapsedMilliseconds < actualDuration)
        {
            var newVolume = (int) (originalVolume + stopWatch.Elapsed.TotalMilliseconds / duration * delta);
            var statusRequest = service.SetVolume(newVolume, token);
            await Task.WhenAll(
                statusRequest,
                Task.Delay(interval, token));
        }

        return await service.SetVolume(targetVolume, token);
    }
}
