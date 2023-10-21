using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using VlcObsService.Vlc.Models;

namespace VlcObsService.Vlc;

public class VlcService
{
    private readonly IOptionsMonitor<VlcServiceOptions> optionsMonitor;
    private readonly HttpClient client;

    public VlcService(IOptionsMonitor<VlcServiceOptions> optionsMonitor, HttpClient client)
    {
        this.optionsMonitor = optionsMonitor;
        this.client = client;
        SetupClient(optionsMonitor);
    }

    public Process? Start()
    {
        var options = optionsMonitor.CurrentValue;
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = options.Path,
            Arguments = $"--extraintf=http --http-host {options.Host} --http-port {options.Port}"
        });
        return process;
    }

    private void SetupClient(IOptionsMonitor<VlcServiceOptions> optionsMonitor)
    {
        var options = optionsMonitor.CurrentValue;
        client.BaseAddress = new Uri($"http://{options.Host}:{options.Port}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + optionsMonitor.CurrentValue.Password)));
    }

    public Task<Status> GetStatus(CancellationToken cancellationToken = default)
        => GetStatus(string.Empty, cancellationToken);

    public async Task<Status> GetStatus(
        string queryString,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync("/requests/status.json" + queryString, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Status>(cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("Vlc Status could not be read");
    }

    public Task<Status> ToggleRandom(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_random", cancellationToken);

    public Task<Status> ToggleRepeat(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_repeat", cancellationToken);

    public Task<Status> Next(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_next", cancellationToken);

    public Task<Status> Play(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_play", cancellationToken);

    public Task<Status> Stop(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_stop", cancellationToken);

    public async Task<Status> SetVolume(int value, CancellationToken cancellationToken = default)
    {
        // Volume status does not return the changed value
        await GetStatus("?command=volume&val=" + value.ToString(), cancellationToken);
        return await GetStatus(cancellationToken);
    }

    public async Task<Status> PlayPlaylist(int id, CancellationToken cancellationToken = default)
    {
        // Playing playlist does not return the changed state
        await GetStatus("?command=pl_play&id=" + id.ToString(), cancellationToken);
        return await GetStatus(cancellationToken);
    }

    public Task Enqueue(string uri, CancellationToken cancellationToken = default)
        => GetStatus("?command=in_enqueue&input=" + uri, cancellationToken);

    public Task Randomize(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_sort&id=0&val=random", cancellationToken);

    public async Task<BrowseResult> Browse(CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(
            "/requests/browse.json?uri=" + optionsMonitor.CurrentValue.FolderUri, 
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<BrowseResult>(cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("Vlc could not be browse uri " + optionsMonitor.CurrentValue.FolderUri);
    }

    public async Task<PlaylistNode> GetPlaylist(CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync("/requests/playlist.json", cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PlaylistNode>(cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("Vlc could not return playlist");
    }
}
