namespace VlcObsService;

public class ObsWorkerOptions
{
    public string? Path { get; init; }
    public string? Url { get; init; }
    public string? Password { get; init; }
    public HashSet<string>? ScenesWithMusic { get; init; }
    public HashSet<string>? SourceKindsWithMusic { get; init; }
}
