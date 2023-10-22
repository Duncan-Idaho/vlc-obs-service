using System.Text.RegularExpressions;

namespace VlcObsService.Vlc.Models;

public static class Extensions
{
    public static Regex AsWildcardRegex(this List<string> extensions)
    {
        var fragments = extensions.Select(extension => $"(.*\\.{Regex.Escape(extension)})");
        return new("^" + string.Join('|', fragments) + "$");
    }

    public static IEnumerable<string> GetFileUris(this IEnumerable<BrowseElement> elements)
        => elements
        .Where(element => element.Type == "file")
        .Select(element => element.Uri);

    public static IEnumerable<string> WhereUriMatches(this IEnumerable<string> elements, Regex filter)
        => elements.Where(uri => filter.IsMatch(uri));

    public static IEnumerable<string> GetFileUris(this IEnumerable<BrowseElement> elements, Regex filter)
        => GetFileUris(elements).WhereUriMatches(filter);
}

