using static VlcObsService.Obs.ObsRepository;
using System.Diagnostics.CodeAnalysis;

namespace VlcObsService.Obs.Models;

public record Input(string Name)
{
    public float? VolumeDb { get; set; }
    public bool? Muted { get; set; }
    public SourceSettings? Settings { get; set; }

    public List<string>? ValidPlaylistItems
        => Settings?.ValidPlaylistItems;

    [MemberNotNullWhen(true, nameof(VolumeDb), nameof(Muted), nameof(ValidPlaylistItems))]
    public bool IsActive
        => VolumeDb >= -60
        && Muted == false
        && ValidPlaylistItems is { Count: > 0 };
}
