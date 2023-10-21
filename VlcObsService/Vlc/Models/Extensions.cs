using System.Text.RegularExpressions;

namespace VlcObsService.Vlc.Models;

public static class Extensions
{
    public static Regex AsWildcardRegex(this string value)
        => new Regex("^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$");

    public static IEnumerable<string> GetFileUris(this IEnumerable<BrowseElement> elements)
        => elements
        .Where(element => element.Type == "file")
        .Select(element => element.Uri);

    public static IEnumerable<string> GetFileUris(this IEnumerable<BrowseElement> elements, Regex filter)
        => GetFileUris(elements)
        .Where(uri => filter.IsMatch(uri));

    public static IEnumerable<string> GetFileUris(this IEnumerable<BrowseElement> elements, string filter)
        => GetFileUris(elements, filter.AsWildcardRegex());
}

