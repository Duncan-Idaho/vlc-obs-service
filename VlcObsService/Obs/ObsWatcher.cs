using OBSWebsocketDotNet;
using System.Collections.Concurrent;
using OBSWebsocketDotNet.Types;
using OBSWebsocketDotNet.Types.Events;
using VlcObsService.Obs.Models;
using Input = VlcObsService.Obs.Models.Input;

namespace VlcObsService.Obs;

public sealed class ObsWatcher : IDisposable
{
    private readonly ILogger<ObsWatcher> _logger;
    private readonly OBSWebsocket _obs = new();
    private readonly ObsRepository _repository;

    public ConcurrentDictionary<string, Scene> Scenes { get; private set; } = new();
    public ConcurrentDictionary<string, Input> Inputs { get; private set; } = new();
    public string? CurrentSceneName { get; private set; } = null;

    public event EventHandler<string>? RecheckSceneNeeded;
    public event EventHandler<string>? VolumeChanged;
    public event EventHandler? Disconnected;

    public bool IsConnected => _obs.IsConnected;
    public TimeSpan WSTimeout => _obs.WSTimeout;

    public HashSet<string> SourceKindsWithMusic { get; private set; } = new();

    public ObsWatcher(ILogger<ObsWatcher> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _repository = new(loggerFactory.CreateLogger<ObsRepository>(), _obs);

        _obs.Connected += Obs_Connected;
        _obs.Disconnected += Obs_Disconnected;
        _obs.CurrentProgramSceneChanged += Obs_CurrentProgramSceneChanged;

        _obs.InputCreated += Obs_InputCreated;
        _obs.InputRemoved += Obs_InputRemoved;
        _obs.InputNameChanged += Obs_InputNameChanged;
        _obs.InputVolumeChanged += Obs_InputVolumeChanged;
        _obs.InputMuteStateChanged += Obs_InputMuteStateChanged;
        _obs.SceneItemEnableStateChanged += Obs_SceneItemEnableStateChanged;

        _obs.SceneItemCreated += Obs_SceneItemCreated;
        _obs.SceneItemRemoved += Obs_SceneItemRemoved;
        _obs.SceneListChanged += Obs_SceneListChanged;
    }

    public void Connect(string url, string password)
        => _obs.ConnectAsync(url, password);

    private void Obs_Connected(object? sender, EventArgs e)
    {
        _logger.LogInformation("Connected");

        RefreshInputs();
        RefreshSceneItems();
        CurrentSceneName = _obs.GetCurrentProgramScene();

        RecheckScene();
    }

    private void Obs_Disconnected(object? sender, OBSWebsocketDotNet.Communication.ObsDisconnectionInfo e)
    {
        _logger.LogInformation("Disconnected");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void Obs_CurrentProgramSceneChanged(object? sender, ProgramSceneChangedEventArgs e)
    {
        this.CurrentSceneName = e.SceneName;
        RecheckScene();
    }

    public void Dispose()
    {

        _obs.Connected -= Obs_Connected;
        _obs.Disconnected -= Obs_Disconnected;
        _obs.CurrentProgramSceneChanged -= Obs_CurrentProgramSceneChanged;

        _obs.InputCreated -= Obs_InputCreated;
        _obs.InputRemoved += Obs_InputRemoved;
        _obs.InputNameChanged -= Obs_InputNameChanged;
        _obs.InputVolumeChanged -= Obs_InputVolumeChanged;
        _obs.InputMuteStateChanged -= Obs_InputMuteStateChanged;
        _obs.SceneItemEnableStateChanged -= Obs_SceneItemEnableStateChanged;

        _obs.SceneItemCreated -= Obs_SceneItemCreated;
        _obs.SceneItemRemoved -= Obs_SceneItemRemoved;
            _obs.SceneListChanged -= Obs_SceneListChanged;
    }

    private void Obs_SceneItemEnableStateChanged(object? sender, SceneItemEnableStateChangedEventArgs e)
    {
        if (!Scenes.TryGetValue(e.SceneName, out var scene))
        {
            _logger.LogWarning("Item enabled status of {inputName} from scene {scene} can't be updated because the scene was not sent by OBS",
                e.SceneItemId,
                e.SceneName);
            return;
        }
        scene.ItemsEnabled[e.SceneItemId] = e.SceneItemEnabled;
        RecheckScene();
    }

    private void RecheckScene()
    {
        if (CurrentSceneName is not null)
            RecheckSceneNeeded?.Invoke(this, CurrentSceneName);
    }


    private void Obs_InputVolumeChanged(object? sender, InputVolumeChangedEventArgs e)
    {
        if (Inputs.TryGetValue(e.Volume.InputName, out var input))
            input.Volume = e.Volume.InputVolumeMul;
        else
            _logger.LogWarning("Volume of {inputName} can't be updated because the input was not sent by OBS", e.Volume.InputName);

        VolumeChanged?.Invoke(this, e.Volume.InputName);
    }

    private void Obs_InputMuteStateChanged(object? sender, InputMuteStateChangedEventArgs e)
    {
        if (Inputs.TryGetValue(e.InputName, out var input))
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
        Inputs.TryRemove(e.InputName, out _);
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
        Scenes = new(_repository.GetScenes().ToDictionary(scene => scene.Name, GetSceneInfo));

        _logger.LogInformation("Scenes refreshed: {scenes}", string.Join(',', Scenes.Values.Select(scene => scene.Name)));

        Scene GetSceneInfo(SceneBasicInfo scene)
        {
            var items = _repository.GetSceneItems(scene.Name);
            var itemsEnabled = items.ToDictionary(
                item => item.ItemId,
                item => _repository.GetSceneItemEnabled(scene.Name, item.ItemId));
            return new(scene.Name, items, new(itemsEnabled));
        }

        RecheckScene();
    }

    private void Obs_InputNameChanged(object? sender, InputNameChangedEventArgs e)
    {
        if (!Inputs.TryRemove(e.OldInputName, out _))
            return;

        RefreshInput(e.InputName);
    }

    public void RefreshInput(string inputName)
    {
        Inputs.AddOrUpdate(inputName,
            GetInput,
            (_, _) => GetInput(inputName));

        RecheckScene();
    }

    public void UpdateSourceKindsWithMusic(HashSet<string> value)
    {
        SourceKindsWithMusic = new(value);
        if (IsConnected)
            RefreshInputs();
    }

    public void RefreshInputs()
    {
        var inputNames = SourceKindsWithMusic
            .SelectMany(kind => _repository.GetInputList(kind))
            .Select(input => input.InputName);

        Inputs = new(inputNames.ToDictionary(name => name, GetInput));
    }

    private Input GetInput(string name)
    {
        return new Input(name)
        {
            Volume = _repository.GetInputVolume(name),
            Muted = _repository.GetInputMute(name),
            Settings = _repository.GetInputSettings(name)
        };
    }
}
