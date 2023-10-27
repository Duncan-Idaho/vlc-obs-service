using Newtonsoft.Json;

namespace VlcObsService.Obs.Models;

public class PlaylistItem
{
    [JsonProperty(PropertyName = "value")]
    public string? Value { get; set; }
}
