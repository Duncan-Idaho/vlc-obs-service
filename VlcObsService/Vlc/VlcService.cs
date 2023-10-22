using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using VlcObsService.Vlc.Models;

namespace VlcObsService.Vlc;

public class VlcService
{
    private readonly IOptionsMonitor<VlcServiceOptions> optionsMonitor;
    private readonly HttpClient client;
    private readonly ILogger<VlcService> logger;
    private readonly string defaultPassword = GeneratePassword(64);

    private static string GeneratePassword(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Convert.ToBase64String(bytes);
    }

    public VlcService(
        IOptionsMonitor<VlcServiceOptions> optionsMonitor, 
        HttpClient client, 
        ILogger<VlcService> logger)
    {
        this.optionsMonitor = optionsMonitor;
        this.client = client;
        this.logger = logger;
        SetupClient(optionsMonitor);
    }

    public Process? Start()
    {
        var options = optionsMonitor.CurrentValue;
        var process = Process.Start(new ProcessStartInfo()
        {
            FileName = Environment.ExpandEnvironmentVariables(options.Path),
            Arguments = $"--extraintf=http --http-host {options.Host} "
                +$"--http-port {options.Port} "
                +$"--http-password \"{options.Password ?? defaultPassword}\" "
                +$"--qt-start-minimized"
        });
        return process;
    }

    private void SetupClient(IOptionsMonitor<VlcServiceOptions> optionsMonitor)
    {
        var options = optionsMonitor.CurrentValue;
        client.BaseAddress = new Uri($"http://{options.Host}:{options.Port}");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + (optionsMonitor.CurrentValue.Password ?? defaultPassword))));
    }

    public Task<Status> GetStatus(CancellationToken cancellationToken = default)
        => GetStatus(string.Empty, cancellationToken);

    private async Task<HttpResponseMessage> GetWithRetry(string uri, CancellationToken cancellationToken = default)
    {
        // VLC tends to reset idle connections. Targetted retries won't hurt.
        const int retryCount = 3;
        for (int currentRetry = 1; currentRetry < retryCount; currentRetry++)
        {
            try
            {
                return await client.GetAsync(uri, cancellationToken);
            }
            catch (HttpRequestException exception) 
            when (exception.InnerException is IOException
            {
                InnerException: SocketException { SocketErrorCode: SocketError.ConnectionReset }
            })
            {
                if (retryCount > 1)
                    logger.LogWarning("More than 1 Connection Reset while sending {uri} to VLC. Retry count {currentRetry}", uri, currentRetry);
            }
        }
        return await client.GetAsync(uri, cancellationToken);
    }

    public async Task<Status> GetStatus(
        string queryString,
        CancellationToken cancellationToken = default)
    {
        using var response = await GetWithRetry("/requests/status.json" + queryString, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<Status>(cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("Vlc Status could not be read");
    }

    public Task<Status> ToggleRandom(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_random", cancellationToken);

    public Task<Status> ToggleRepeat(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_repeat", cancellationToken);

    public Task<Status> ToggleLoop(CancellationToken cancellationToken = default)
        => GetStatus("?command=pl_loop", cancellationToken);

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

    public Task<BrowseResult> Browse(CancellationToken cancellationToken = default)
        => Browse(optionsMonitor.CurrentValue.FolderUri, cancellationToken);

    public async Task<BrowseResult> Browse(string? uri, CancellationToken cancellationToken = default)
    {
        if (uri is null)
            return new BrowseResult(Array.Empty<BrowseElement>());

        using var response = await GetWithRetry(
            "/requests/browse.json?uri=" + uri,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        try
        {
            return (await response.Content.ReadFromJsonAsync<BrowseResult>(cancellationToken: cancellationToken))
                ?? throw new InvalidOperationException("Received null result");
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Couldn't browse URI {uri}", uri);
            return new BrowseResult(Array.Empty<BrowseElement>());
        }
    }

    public Regex GetFilter()
        => optionsMonitor.CurrentValue.Extensions.AsWildcardRegex();

    public async Task<PlaylistNode> GetPlaylist(CancellationToken cancellationToken = default)
    {
        using var response = await GetWithRetry("/requests/playlist.json", cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<PlaylistNode>(cancellationToken: cancellationToken))
            ?? throw new InvalidOperationException("Vlc could not return playlist");
    }
}
