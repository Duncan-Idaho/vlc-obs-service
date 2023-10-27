using Newtonsoft.Json;

namespace VlcObsService.Obs.Models;

public class SourceSettings
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
