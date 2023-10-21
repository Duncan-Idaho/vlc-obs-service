using System;

namespace VlcObsService.Vlc.Models;

public record PlaylistNode(
    string Name,
    int Id,
    PlaylistNode[]? Children,
    int? Duration,
    string? Uri,
    string? Current)
{
    public PlaylistNode[]? EnqueuedItems
    {
        get
        {
            return this switch
            {
                { Id: 0 } => Children?.FirstOrDefault(child => child.Id == 1)?.EnqueuedItems,
                { Id: 1 } => Children,
                _ => null
            };
        }
    }
}
