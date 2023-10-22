using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using VlcObsService.Obs;
using VlcObsService.Vlc;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VlcObsService
{
    public sealed class ObsWorker : BackgroundService
    {
        private readonly ILogger<ObsWorker> _logger;
        private readonly OBSWebsocket obs = new ();
        private readonly VlcInstanceManager vlcWorker;

        private readonly IOptionsMonitor<ObsWorkerOptions> optionsMonitor;
        private readonly IOptionsMonitor<ObsApplicationWebSocketOptions> obsOptionsMonitor;
        private readonly IDisposable? optionsMonitorChangeToken;

        private Dictionary<string, List<string>> playlistForScene = new();

        public ObsWorker(
            ILogger<ObsWorker> logger, 
            VlcInstanceManager vlcWorker,
            IOptionsMonitor<ObsWorkerOptions> optionsMonitor,
            IOptionsMonitor<ObsApplicationWebSocketOptions> obsOptionsMonitor)
        {
            _logger = logger;
            obs.Connected += Obs_Connected;
            obs.Disconnected += Obs_Disconnected;
            obs.CurrentProgramSceneChanged += Obs_CurrentProgramSceneChanged;
            obs.SceneItemCreated += Obs_SceneItemCreated;
            obs.SceneItemRemoved += Obs_SceneItemRemoved;

            this.vlcWorker = vlcWorker;
            this.optionsMonitor = optionsMonitor;
            this.obsOptionsMonitor = obsOptionsMonitor;

            this.optionsMonitorChangeToken = this.optionsMonitor.OnChange((_, _) =>
            {
                RefreshSceneItems();
            });
        }

        public override void Dispose()
        {
            base.Dispose();
            optionsMonitorChangeToken?.Dispose();

        }

        public void RefreshSceneItems()
        {
            playlistForScene = optionsMonitor.CurrentValue.SourceKindsWithMusic switch
            {
                { Count: > 0 } sourceKinds => GetMusicWitSourceKinds(sourceKinds),
                _ => new()
            };

            _logger.LogInformation("Scenes with music source kinds refreshed: {scenes}",
                string.Join(',', playlistForScene.Keys));

            Dictionary<string, List<string>> GetMusicWitSourceKinds(HashSet<string> sourceKinds)
            {
                return GetScenes()
                    .Select(scene => scene.Name)
                    .ToDictionary(
                        sceneName => sceneName,
                        sceneName => GetPlaylistFromScenes(sceneName, sourceKinds));
            }

            List<string> GetPlaylistFromScenes(string sceneName, HashSet<string> sourceKinds)
                => GetSceneItems(sceneName)
                    .Where(item => sourceKinds.Contains(item.SourceKind))
                    .SelectMany(item => GetPlaylistFromInput(item.SourceName))
                    .Select(playlist => playlist.Value)
                    .Where(value => value is not null)
                    .ToList()!;
        }

        private IEnumerable<SceneBasicInfo> GetScenes()
        {
            try
            {
                return obs.GetSceneList().Scenes;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requesting scene list from OBS");
                return Enumerable.Empty<SceneBasicInfo>();
            }
        }

        private IEnumerable<SceneItemDetails> GetSceneItems(string scene)
        {
            try
            {
                var items = obs.GetSceneItemList(scene);
                return items;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested items for OBS scene {scene}", scene);
                return Enumerable.Empty<SceneItemDetails>();
            }
        }

        private IEnumerable<PlaylistItem> GetPlaylistFromInput(string inputName)
        {
            try
            {
                return obs.GetInputSettings(inputName).Settings["playlist"]?.ToObject<List<PlaylistItem>>()
                    ?? new();
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested playlist for OBS input {inputName}", inputName);
                return Enumerable.Empty<PlaylistItem>();
            }
        }

        private class PlaylistItem
        {
            [JsonProperty(PropertyName = "value")]
            public string? Value { get; set; }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!obs.IsConnected)
                    {
                        var options = this.optionsMonitor.CurrentValue;
                        var obsOptions = this.obsOptionsMonitor.CurrentValue;

                        var url = options.Url ?? obsOptions.BuildLocalhostWebSocketUrl();
                        var password = options.Password ?? obsOptions.ServerPassword;

                        if (url is not null && password is not null)
                        {
                            obs.ConnectAsync(url, password);
                            _logger.LogInformation("Waiting for OBS...");
                        }
                        else
                        {
                            _logger.LogInformation("There is no configuration path for OBS nor any port or password configuration");
                        }
                    }
                    await Task.Delay(obs.WSTimeout, stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                Program.ExitCode = 1;
                throw;
            }
        }

        private void Obs_CurrentProgramSceneChanged(object? sender, OBSWebsocketDotNet.Types.Events.ProgramSceneChangedEventArgs e)
        {
            HandleScene(e.SceneName);
        }

        private void Obs_Disconnected(object? sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
        {
            _logger.LogInformation("Disconnected");
            StartAndLogFailure(() => vlcWorker.EnsureClosedAsync(), "closing");
        }

        private void Obs_Connected(object? sender, EventArgs e)
        {
            _logger.LogInformation("Connected");
            RefreshSceneItems();
            HandleScene(obs.GetCurrentProgramScene());
        }

        private void Obs_SceneItemRemoved(object? sender, SceneItemRemovedEventArgs e)
        {
            RefreshSceneItems();
        }

        private void Obs_SceneItemCreated(object? sender, SceneItemCreatedEventArgs e)
        {
            RefreshSceneItems();
        }

        private void HandleScene(string scene)
        {
            var playlist = GetPlaylistForScene(scene);
            if (playlist is not null)
            {
                _logger.LogInformation("Scene {scene} requires playing", scene);
                StartAndLogFailure(() => vlcWorker.PlayAsync(playlist), "playing");
            }
            else
            {
                _logger.LogInformation("Scene {scene} requires stopping", scene);
                StartAndLogFailure(() => vlcWorker.StopAsync(), "stopping");
            }
        }

        /// <summary>
        /// Get the playlist for the scene
        /// </summary>
        /// <param name="scene">name of the scene</param>
        /// <returns>null if no music, empty if music from config, list of music if music from OBS</returns>
        private List<string>? GetPlaylistForScene(string scene)
        {
            var scenes = optionsMonitor.CurrentValue.ScenesWithMusic;
            if (scenes?.Count > 0)
                return scenes.Contains(scene) ? new() : null;

            return playlistForScene.TryGetValue(scene, out var value)
                ? value
                : null;
        }

        public async void StartAndLogFailure(Func<Task> task, string request)
        {
            try
            {
                await task();
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation("Cancelled VLC {request}", request);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error in VLC {request}", request);
            }
        }
    }
}