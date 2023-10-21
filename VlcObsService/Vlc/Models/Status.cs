namespace VlcObsService.Vlc.Models;

public record Status(
    int? Currentplid,
    int Volume,
    bool Random,
    string State,
    bool Repeat);
