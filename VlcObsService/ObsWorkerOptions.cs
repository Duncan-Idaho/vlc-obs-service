namespace VlcObsService;

public class ObsWorkerOptions
{
    public required string Url { get; init; }
    public required string Password { get; init; }
    public required HashSet<string> ScenesWithMusic { get; init; }
}
