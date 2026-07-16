using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>The first-party view of a codebase: files that are neither tests nor vendored/
/// minified assets. Size and language figures built from this describe the project's own code,
/// not its tests or bundled libraries — so a C# tool that vendors a large JS bundle still reads
/// as a C# project. Pure; callers surface the excluded test/vendored totals as a breakdown.</summary>
public static class CodebaseStats
{
    public static bool IsFirstParty(FileNode f) => !f.IsTest && !f.IsVendored;

    public static int FirstPartyLoc(ProjectModel model) => model.Files.Where(IsFirstParty).Sum(f => f.Loc);
    public static int TestLoc(ProjectModel model) => model.Files.Where(f => f.IsTest).Sum(f => f.Loc);
    public static int VendoredLoc(ProjectModel model) => model.Files.Where(f => f.IsVendored).Sum(f => f.Loc);

    /// <summary>Per-language LOC over first-party files only, keyed exactly like
    /// <see cref="ProjectModel.LanguageLoc"/> (by <see cref="FileNode.Language"/>).</summary>
    public static Dictionary<string, int> FirstPartyLanguageLoc(ProjectModel model)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in model.Files.Where(f => IsFirstParty(f) && f.Loc > 0))
        {
            d[f.Language] = d.GetValueOrDefault(f.Language) + f.Loc;
        }
        return d;
    }

    /// <summary>Dominant first-party language, or "mixed" when nothing scores. Used for the
    /// "X codebase" narrative so it reflects the project, not a vendored bundle.</summary>
    public static string PrimaryLanguage(ProjectModel model)
    {
        var d = FirstPartyLanguageLoc(model);
        return d.Count == 0 ? "mixed"
            : d.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
    }
}
