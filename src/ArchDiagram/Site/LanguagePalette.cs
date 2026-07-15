namespace ArchDiagram.Site;

/// <summary>Deterministic per-language colours, shared by the Overview language bar and the
/// Structure treemap so a language reads the same colour everywhere. Unknown languages cycle
/// a small fallback palette via a caller-held index.</summary>
public static class LanguagePalette
{
    public static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C#"] = "#2f6fab", ["TypeScript/JavaScript"] = "#e8b73a", ["Python"] = "#3572A5",
        ["PowerShell"] = "#6b46c1", ["SQL"] = "#c0392b", ["HTML"] = "#e34c26", ["CSS"] = "#563d7c",
        ["JSON"] = "#8a8a8a", ["YAML"] = "#6fbf73", ["XML"] = "#0060ac", ["Markdown"] = "#4a4a4a",
        ["MSBuild"] = "#68217a", ["Razor"] = "#512bd4", ["Protobuf"] = "#4d7e65",
    };

    private static readonly string[] Fallback = ["#1f8a8a", "#b7791f", "#7a5195", "#ef5675", "#488f31"];

    /// <summary>Colour for a language; unknowns take the next fallback (index advanced).</summary>
    public static string ColorFor(string language, ref int fallbackIndex) =>
        Colors.TryGetValue(language, out var c) ? c : Fallback[fallbackIndex++ % Fallback.Length];

    /// <summary>Stable colour for a language with no shared fallback index (treemap use):
    /// unknown languages hash to a fallback slot so the choice is deterministic per language.</summary>
    public static string ColorFor(string language)
    {
        if (Colors.TryGetValue(language, out var c)) { return c; }
        var h = 0;
        foreach (var ch in language) { h = (h * 31 + ch) & 0x7fffffff; }
        return Fallback[h % Fallback.Length];
    }
}
