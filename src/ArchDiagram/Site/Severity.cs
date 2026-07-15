namespace ArchDiagram.Site;

/// <summary>Maps a complexity score to a human severity band ("level n") and a
/// badge CSS modifier. Cyclomatic and cognitive share the same coarse bands, kept
/// wide so the cognitive walker's approximation never changes the displayed level.
/// Uses only existing badge classes (site.css:92-96) — no new CSS.</summary>
internal static class Severity
{
    /// <summary>Cognitive/cyclomatic threshold at/above which a method is
    /// considered "complex" for tiles and inline snippets (SonarQube default gate).</summary>
    internal const int HighThreshold = 11;

    /// <summary>SonarQube's default cognitive-complexity gate, used only for the tile label.</summary>
    internal const int SonarGate = 15;

    internal static (string Label, string Cls) Band(int score) => score switch
    {
        <= 5 => ("Low", "ok"),
        <= 10 => ("Moderate", ""),
        <= 20 => ("High", "warn"),
        _ => ("Very High", "warn"),
    };

    /// <summary>A "{score} · Level" badge using the existing .badge classes.</summary>
    internal static string Badge(int score)
    {
        var (label, cls) = Band(score);
        return $"<span class=\"badge {cls}\">{score} · {Html.Encode(label)}</span>";
    }
}
