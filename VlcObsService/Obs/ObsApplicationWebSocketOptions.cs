namespace VlcObsService.Obs;

public record ObsApplicationWebSocketOptions
{
    public int? ServerPort { get; init; }
    public string? ServerPassword { get; init; }

    public string? BuildLocalhostWebSocketUrl()
        => ServerPort is not null
        ? "ws://localhost:" + ServerPort
        : null;
}
