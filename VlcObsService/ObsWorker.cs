using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using VlcObsService.Obs;
using VlcObsService.Vlc;
using static System.Formats.Asn1.AsnWriter;

namespace VlcObsService
{
    public sealed class ObsWorker : BackgroundService
    {
        private readonly ILogger<ObsWorker> _logger;
        private readonly OBSWebsocket obs = new ();
        private readonly VlcInstanceManager vlcWorker;

        private readonly IOptionsMonitor<ObsWorkerOptions> optionsMonitor;
        private readonly IOptionsMonitor<ObsApplicationWebSocketOptions> obsOptionsMonitor;

        private ConcurrentDictionary<string, Scene> scenes = new();
        private ConcurrentDictionary<string, Input> inputs = new();
        private string? currentSceneName = null;
        private IReadOnlyList<Input> inputsInPlay = new List<Input>();

        private record Scene(
            string Name,
            List<SceneItemDetails> Items,
            ConcurrentDictionary<int, bool?> ItemsEnabled);

        private record Input(string Name)
        {
            public float? Volume { get; set; }
            public bool? Muted { get; set; }
            public SourceSettings? Settings { get; set; }

            public List<string>? ValidPlaylistItems
                => Settings?.ValidPlaylistItems;

            [MemberNotNullWhen(true, nameof(Volume), nameof(Muted), nameof(ValidPlaylistItems))]
            public bool IsActive
                => Volume > 0
                && Muted == false
                && ValidPlaylistItems is { Count: > 0 };
        }

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

            obs.InputCreated += Obs_InputCreated;
            obs.InputRemoved += Obs_InputRemoved;
            obs.InputNameChanged += Obs_InputNameChanged;
            obs.InputVolumeChanged += Obs_InputVolumeChanged;
            obs.InputMuteStateChanged += Obs_InputMuteStateChanged;
            obs.SceneItemEnableStateChanged += Obs_SceneItemEnableStateChanged;

            obs.SceneItemCreated += Obs_SceneItemCreated;
            obs.SceneItemRemoved += Obs_SceneItemRemoved;
            obs.SceneListChanged += Obs_SceneListChanged;

            this.vlcWorker = vlcWorker;
            this.optionsMonitor = optionsMonitor;
            this.obsOptionsMonitor = obsOptionsMonitor;
        }

        private void Obs_SceneItemEnableStateChanged(object? sender, SceneItemEnableStateChangedEventArgs e)
        {
            if (!scenes.TryGetValue(e.SceneName, out var scene))
            {
                _logger.LogWarning("Item enabled status of {inputName} from scene {scene} can't be updated because the scene was not sent by OBS", e.SceneName);
                return;
            }
            scene.ItemsEnabled[e.SceneItemId] = e.SceneItemEnabled;
            RecheckScene();
        }

        private void Obs_InputVolumeChanged(object? sender, InputVolumeChangedEventArgs e)
        {
            if (inputs.TryGetValue(e.Volume.InputName, out var input))
                input.Volume = e.Volume.InputVolumeMul;
            else
                _logger.LogWarning("Volume of {inputName} can't be updated because the input was not sent by OBS", e.Volume.InputName);

            HandleVolumeChange(e.Volume.InputName);
        }

        private void Obs_InputMuteStateChanged(object? sender, InputMuteStateChangedEventArgs e)
        {
            if (inputs.TryGetValue(e.InputName, out var input))
                input.Muted = e.InputMuted;
            else
                _logger.LogWarning("Mute status of {inputName} can't be updated because the input was not sent by OBS", e.InputName);

            RecheckScene();
        }


        private void Obs_InputCreated(object? sender, InputCreatedEventArgs e)
        {
            RefreshInput(e.InputName);
        }

        private void Obs_InputRemoved(object? sender, InputRemovedEventArgs e)
        {
            inputs.TryRemove(e.InputName, out _);
            RecheckScene();
        }

        private void Obs_SceneItemRemoved(object? sender, SceneItemRemovedEventArgs e)
        {
            RefreshSceneItems();
        }

        private void Obs_SceneItemCreated(object? sender, SceneItemCreatedEventArgs e)
        {
            RefreshSceneItems();
        }

        private void Obs_SceneListChanged(object? sender, SceneListChangedEventArgs e)
        {
            RefreshSceneItems();
        }

        public void RefreshSceneItems()
        {
            scenes = new(GetScenes().ToDictionary(scene => scene.Name, GetSceneInfo));

            _logger.LogInformation("Scenes refreshed: {scenes}", string.Join(',', scenes.Values.Select(scene => scene.Name)));

            Scene GetSceneInfo(SceneBasicInfo scene)
            {
                var items = GetSceneItems(scene.Name);
                var itemsEnabled = items.ToDictionary(
                    item => item.ItemId,
                    item => GetSceneItemEnabled(scene.Name, item.ItemId));
                return new(scene.Name, items, new(itemsEnabled));
            }

            RecheckScene();
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

        private bool? GetSceneItemEnabled(string sceneName, int itemId)
        {
            try
            {
                return obs.GetSceneItemEnabled(sceneName, itemId);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requesting scene list from OBS");
                return null;
            }
        }

        private List<SceneItemDetails> GetSceneItems(string scene)
        {
            try
            {
                var items = obs.GetSceneItemList(scene);
                return items;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested items for OBS scene {scene}", scene);
                return new();
            }
        }

        private void Obs_InputNameChanged(object? sender, InputNameChangedEventArgs e)
        {
            if (!inputs.TryRemove(e.OldInputName, out _))
                return;

            RefreshInput(e.InputName);
        }

        public void RefreshInput(string inputName)
        {
            inputs.AddOrUpdate(inputName,
                GetInput,
                (_, _) => GetInput(inputName));

            RecheckScene();
        }

        public override void Dispose()
        {
            base.Dispose();
        }

        public void RefreshInputs()
        {
            var inputNames = obs.GetInputList().Select(input => input.InputName);
            inputs = new(inputNames.ToDictionary(name => name, GetInput));
        }

        private Input GetInput(string name)
        {
            return new Input(name)
            {
                Volume = GetInputVolume(name),
                Muted = GetInputMute(name),
                Settings = GetInputSettings(name)
            };
        }

        private SourceSettings GetInputSettings(string inputName)
        {
            try
            {
                return obs.GetInputSettings(inputName).Settings.ToObject<SourceSettings>()
                    ?? new SourceSettings();
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested playlist for OBS input {inputName}", inputName);
                return new SourceSettings();
            }
        }

        private float? GetInputVolume(string inputName)
        {
            try
            {
                return obs.GetInputVolume(inputName).VolumeMul;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested playlist for OBS input {inputName}", inputName);
                return null;
            }
        }

        private bool? GetInputMute(string inputName)
        {
            try
            {
                return obs.GetInputMute(inputName);
            }
            catch (Exception error)
            {
                _logger.LogError(error, "Error requested playlist for OBS input {inputName}", inputName);
                return null;
            }
        }

        private class SourceSettings
        {
            [JsonProperty(PropertyName = "playlist")]
            public List<PlaylistItem>? PlaylistItems { get; set; }

            [JsonIgnore]
            public List<string> ValidPlaylistItems 
                => PlaylistItems
                ?.Where(item => item.Value is not null)
                .Select(item => item.Value!)
                .ToList()
                ?? new List<string>();

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

        private void Obs_CurrentProgramSceneChanged(object? sender, ProgramSceneChangedEventArgs e)
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
            RefreshInputs();
            RefreshSceneItems();
            HandleScene(obs.GetCurrentProgramScene());
        }

        private void RecheckScene()
        {
            if (currentSceneName is not null)
                HandleScene(currentSceneName);
        }

        private void HandleScene(string scene)
        {
            currentSceneName = scene;
            var playAction = GetPlayActionForInput(scene)
                ?? GetPlayActionForScene(scene);

            if (playAction is not null)
            {
                _logger.LogInformation("Scene {scene} requires playing", scene);
                StartAndLogFailure(() => vlcWorker.PlayAsync(playAction.Value.Playlist, playAction.Value.Volume), "playing");
            }
            else
            {
                _logger.LogInformation("Scene {scene} requires stopping", scene);
                StartAndLogFailure(() => vlcWorker.StopAsync(), "stopping");
            }
        }

        private void HandleVolumeChange(string inputName)
        {
            var inputsInPlay = this.inputsInPlay;
            if (!inputsInPlay.Any(input => input.Name == inputName))
                return;

            var (playlist, volume) = GetPlayActionForInputsInPlay(inputsInPlay);
            _logger.LogInformation("Volume change requested from input {inputName}", inputName);
            
            StartAndLogFailure(() => vlcWorker.PlayAsync(playlist, volume), "playing");
        }

        private (List<string> Playlist, int Volume)? GetPlayActionForScene(string scene)
        {
            var scenesWithMusic = optionsMonitor.CurrentValue.ScenesWithMusic;
            if (scenesWithMusic is not { Count: > 0 })
                return null;

            return scenesWithMusic.Contains(scene)
                ? (new(), 256)
                : null;
        }

        private (List<string> Playlist, int Volume)? GetPlayActionForInput(string sceneName)
        {
            var sourceKindsWithMusic = optionsMonitor.CurrentValue.SourceKindsWithMusic;
            if (sourceKindsWithMusic is not { Count: > 0 })
                return null;

            inputsInPlay = GetInputsForScene(sceneName)
                .Where(item => item.SourceType == SceneItemSourceType.OBS_SOURCE_TYPE_INPUT)
                .Where(input => sourceKindsWithMusic.Contains(input.SourceKind))
                .Select(item => item.SourceName)
                .Select(GetInputOrDefault)
                .Where(input => input.IsActive)
                .ToList();

            if (inputsInPlay.Count == 0)
                return null;

            return GetPlayActionForInputsInPlay(inputsInPlay);

            IEnumerable<SceneItemDetails> GetInputsForScene(string sceneName)
            {
                if (!scenes.TryGetValue(sceneName, out var scene))
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

            Input GetInputOrDefault(string inputName)
            {
                if (!inputs.TryGetValue(inputName, out var input))
                {
                    _logger.LogWarning("Input {inputName} was not received from OBS, yet its active state changed", inputName);
                    return new Input(inputName);
                }

                return input;
            }
        }

        private static (List<string> Playlist, int Volume) GetPlayActionForInputsInPlay(IReadOnlyList<Input> inputsInPlay)
        {
            var playlists = inputsInPlay.SelectMany(input => input.ValidPlaylistItems!).ToList();
            var volume = (int)(inputsInPlay.Average(input => input.Volume)! * 256);

            return (playlists, volume);
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