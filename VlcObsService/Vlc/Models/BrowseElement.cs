namespace VlcObsService.Vlc.Models;

public record BrowseElement(
    string Type, // file or dir
    string Name,
    string Uri);


