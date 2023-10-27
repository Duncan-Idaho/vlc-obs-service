using static VlcObsService.Obs.ObsRepository;
using System.Diagnostics.CodeAnalysis;

namespace VlcObsService.Obs.Models;

public record Input(string Name)
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
