using Microsoft.Extensions.Options;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using VlcObsService.Obs;
using VlcObsService.Vlc;

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

        private HashSet<string> scenesWithMusicSourceKinds = new();

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
            scenesWithMusicSourceKinds = optionsMonitor.CurrentValue.SourceKindsWithMusic switch
            {
                { Count: > 0 } sourceKinds => GetMusicWitSourceKinds(sourceKinds),
                _ => new HashSet<string>()
            };

            _logger.LogInformation("Scenes with music source kinds refreshed: {scenes}",
                string.Join(',', scenesWithMusicSourceKinds));

            HashSet<string> GetMusicWitSourceKinds(HashSet<string> sourceKinds)
            {
                return GetScenes()
                    .Select(scene => scene.Name)
                    .Where(sceneName => IsSceneWithMusicSourceKinds(sceneName, sourceKinds))
                    .ToHashSet();
            }

            bool IsSceneWithMusicSourceKinds(string sceneName, HashSet<string> sourceKinds)
                => GetSceneItems(sceneName).Any(item => sourceKinds.Contains(item.SourceKind));
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
                _logger.LogError(error, "Error requested scenes for OBS");
                return Enumerable.Empty<SceneItemDetails>();
            }
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
            if (ShouldPlayMusic(scene))
            {
                _logger.LogInformation("Scene {scene} requires playing", scene);
                StartAndLogFailure(() => vlcWorker.PlayAsync(), "playing");
            }
            else
            {
                _logger.LogInformation("Scene {scene} requires stopping", scene);
                StartAndLogFailure(() => vlcWorker.StopAsync(), "stopping");
            }
        }

        private bool ShouldPlayMusic(string scene)
        {
            var scenes = optionsMonitor.CurrentValue.ScenesWithMusic;
            if (scenes?.Count > 0)
                return scenes.Contains(scene);

            return scenesWithMusicSourceKinds.Contains(scene);
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