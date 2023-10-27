using System.Collections.Concurrent;
using OBSWebsocketDotNet.Types;

namespace VlcObsService.Obs.Models;

public record Scene(
    string Name,
    List<SceneItemDetails> Items,
    ConcurrentDictionary<int, bool?> ItemsEnabled);
