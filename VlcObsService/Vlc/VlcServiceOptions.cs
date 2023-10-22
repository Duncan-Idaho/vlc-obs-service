namespace VlcObsService.Vlc;

public class VlcServiceOptions
{
    public required string Path { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Password { get; set; }
    public string? FolderUri { get; init; }
    public List<string> Extensions { get; init; } = new();

}
