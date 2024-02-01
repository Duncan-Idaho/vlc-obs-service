using OBSWebsocketDotNet;
using OBSWebsocketDotNet.Types;
using VlcObsService.Obs.Models;

namespace VlcObsService.Obs;

public class ObsRepository
{
    private readonly ILogger<ObsRepository> _logger;
    private readonly OBSWebsocket obs;

    public ObsRepository(ILogger<ObsRepository> logger, OBSWebsocket obs)
    {
        _logger = logger;
        this.obs = obs;
    }

    public IEnumerable<SceneBasicInfo> GetScenes()
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

    public bool? GetSceneItemEnabled(string sceneName, int itemId)
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

    public List<SceneItemDetails> GetSceneItems(string scene)
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

    public List<InputBasicInfo> GetInputList(string? inputKind = null)
    {
        try
        {
            return obs.GetInputList(inputKind);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Error requested inputs list from OBS");
            return new();
        }
    }

    public SourceSettings GetInputSettings(string inputName)
    {
        try
        {
            return obs.GetInputSettings(inputName).Settings.ToObject<SourceSettings>()
                ?? new SourceSettings();
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Error requesting playlist for OBS input {inputName}", inputName);
            return new SourceSettings();
        }
    }

    public float? GetInputVolume(string inputName)
    {
        try
        {
            return obs.GetInputVolume(inputName).VolumeMul;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Error requsting volume for OBS input {inputName}", inputName);
            return null;
        }
    }

    public bool? GetInputMute(string inputName)
    {
        try
        {
            return obs.GetInputMute(inputName);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Error requesting mute status for OBS input {inputName}", inputName);
            return null;
        }
    }
}
