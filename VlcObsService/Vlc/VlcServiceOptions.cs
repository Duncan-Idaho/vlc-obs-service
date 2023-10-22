﻿namespace VlcObsService.Vlc;

public class VlcServiceOptions
{
    public required string Path { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public string? Password { get; set; }
    public required string FolderUri { get; init; }
}
