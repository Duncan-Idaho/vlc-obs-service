using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using VlcObsService.Obs;
using VlcObsService.Vlc;
using Input = VlcObsService.Obs.Models.Input;

namespace VlcObsService
{
    public sealed class ObsWorker : BackgroundService
    {
        private readonly ILogger<ObsWorker> _logger;
        private readonly ObsWatcher _obs;
        private readonly VlcInstanceManager _vlc;

        private readonly IOptionsMonitor<ObsWorkerOptions> _optionsMonitor;
        private readonly IOptionsMonitor<ObsApplicationWebSocketOptions> _obsOptionsMonitor;
        private readonly IDisposable? _optionsMonitorWatcher;

        private IReadOnlyList<Input> _inputsInPlay = new List<Input>();

        public ObsWorker(
            ILogger<ObsWorker> logger,
            ObsWatcher state,
            VlcInstanceManager vlc,
            IOptionsMonitor<ObsWorkerOptions> optionsMonitor,
            IOptionsMonitor<ObsApplicationWebSocketOptions> obsOptionsMonitor)
        {
            _logger = logger;

            _obs = state;
            _obs.RecheckSceneNeeded += HandleScene;
            _obs.VolumeChanged += HandleVolumeChange;
            _obs.Disconnected += HandleDisconnected;

            _vlc = vlc;
            _optionsMonitor = optionsMonitor;
            _obsOptionsMonitor = obsOptionsMonitor;

            _optionsMonitorWatcher = _optionsMonitor.OnChange(value 
                => _obs.UpdateSourceKindsWithMusic(value.SourceKindsWithMusic ?? new()));
        }

        public override void Dispose()
        {
            base.Dispose();
            _obs.RecheckSceneNeeded -= HandleScene;
            _obs.VolumeChanged -= HandleVolumeChange;
            _obs.Disconnected -= HandleDisconnected;
            _optionsMonitorWatcher?.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!_obs.IsConnected)
                    {
                        var options = _optionsMonitor.CurrentValue;
                        var obsOptions = _obsOptionsMonitor.CurrentValue;

                        var url = options.Url ?? obsOptions.BuildLocalhostWebSocketUrl();
                        var password = options.Password ?? obsOptions.ServerPassword;

                        if (url is not null && password is not null)
                        {
                            _obs.UpdateSourceKindsWithMusic(options.SourceKindsWithMusic ?? new());
                            _obs.Connect(url, password);
                            _logger.LogInformation("Waiting for OBS...");
                        }
                        else
                        {
                            _logger.LogInformation("There is no configuration path for OBS nor any port or password configuration");
                        }
                    }
                    await Task.Delay(_obs.WSTimeout, stoppingToken);
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

        private void HandleScene(object? sender, string scene)
        {
            var playAction = GetPlayActionForInput(scene)
                ?? GetPlayActionForScene(scene);

            if (playAction is not null)
            {
                _logger.LogInformation("Scene {scene} requires playing", scene);
                StartAndLogFailure(() => _vlc.PlayAsync(playAction.Value.Playlist, playAction.Value.Volume), "playing");
            }
            else
            {
                _logger.LogInformation("Scene {scene} requires stopping", scene);
                StartAndLogFailure(() => _vlc.StopAsync(), "stopping");
            }
        }

        private void HandleVolumeChange(object? sender, string inputName)
        {
            var inputsInPlay = _inputsInPlay;
            if (!inputsInPlay.Any(input => input.Name == inputName))
                return;

            var (playlist, volume) = GetPlayActionForInputsInPlay(inputsInPlay);
            _logger.LogInformation("Volume change requested from input {inputName}", inputName);
            
            StartAndLogFailure(() => _vlc.PlayAsync(playlist, volume), "playing");
        }

        private (List<string> Playlist, int Volume)? GetPlayActionForScene(string scene)
        {
            var scenesWithMusic = _optionsMonitor.CurrentValue.ScenesWithMusic;
            if (scenesWithMusic is not { Count: > 0 })
                return null;

            return scenesWithMusic.Contains(scene)
                ? (new(), 256)
                : null;
        }

        private (List<string> Playlist, int Volume)? GetPlayActionForInput(string sceneName)
        {
            var sourceKindsWithMusic = _optionsMonitor.CurrentValue.SourceKindsWithMusic;
            if (sourceKindsWithMusic is not { Count: > 0 })
                return null;

            _inputsInPlay = GetInputsForScene(sceneName)
                .Where(item => item.SourceType == SceneItemSourceType.OBS_SOURCE_TYPE_INPUT)
                .Where(input => sourceKindsWithMusic.Contains(input.SourceKind))
                .Select(item => item.SourceName)
                .Select(GetInputOrDefault)
                .Where(input => input.IsActive)
                .ToList();

            if (_inputsInPlay.Count == 0)
                return null;

            return GetPlayActionForInputsInPlay(_inputsInPlay);
        }

        private Input GetInputOrDefault(string inputName)
        {
            if (!_obs.Inputs.TryGetValue(inputName, out var input))
            {
                _logger.LogWarning("Input {inputName} was not received from OBS, yet its active state changed", inputName);
                return new Input(inputName);
            }

            return input;
        }

        private IEnumerable<SceneItemDetails> GetInputsForScene(string sceneName)
        {
            if (!_obs.Scenes.TryGetValue(sceneName, out var scene))
            {
                _logger.LogWarning("Scene {sceneName} was not received from OBS, yet it is now visible", sceneName);
                return Enumerable.Empty<SceneItemDetails>();
            }

            var inputsFromChildScenes = scene.Items
                .Where(item => item.SourceType == SceneItemSourceType.OBS_SOURCE_TYPE_SCENE)
                .SelectMany(input => GetInputsForScene(input.SourceName));

            return scene.Items
                .Where(item => scene.ItemsEnabled.TryGetValue(item.ItemId, out var enabled) && enabled == true)
                .Concat(inputsFromChildScenes);
        }

        private static (List<string> Playlist, int Volume) GetPlayActionForInputsInPlay(IReadOnlyList<Input> inputsInPlay)
        {
            var playlists = inputsInPlay.SelectMany(input => input.ValidPlaylistItems!).ToList();
            var volume = (int)(inputsInPlay.Average(input => input.Volume)! * 256);

            return (playlists, volume);
        }

        private void HandleDisconnected(object? sender, EventArgs e)
        {
            StartAndLogFailure(() => _vlc.EnsureClosedAsync(), "closing");
        }

        private async void StartAndLogFailure(Func<Task> task, string request)
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