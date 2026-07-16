using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Deterministic, template-generated prose (no LLM, no network) describing a
/// codebase and its folders, composed only from data already in the model. Clauses are
/// assembled in a list and joined so a missing part never leaves a dangling conjunction.</summary>
public static class NarrativeBuilder
{
    /// <summary>One-paragraph overview of the whole project. <paramref name="linkTopFiles"/>
    /// receives the top few central files so a caller can render them as links; the returned
    /// text names them in plain form for the markdown export.</summary>
    public static string ProjectSummary(ProjectModel model)
    {
        var clauses = new List<string>();
        var fileCount = model.Files.Count;
        // Describe the project by its own (first-party) code, not vendored bundles or tests —
        // otherwise a C# tool that ships a large JS library reads as a JS project.
        var loc = CodebaseStats.FirstPartyLoc(model);
        var primary = CodebaseStats.PrimaryLanguage(model);
        var topFolders = model.Files.Select(TopFolder).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        clauses.Add($"{model.RootName} is a {primary} codebase of {fileCount:N0} file(s) ({loc:N0} lines) "
                  + $"across {topFolders:N0} top-level area(s)");

        if (model.Projects.Count > 0)
        {
            clauses.Add($"it is organised into {model.Projects.Count:N0} .NET project(s)");
        }

        var packages = TopExternalPackages(model, 3);
        if (packages.Count > 0)
        {
            clauses.Add($"it leans on {JoinList(packages)}");
        }

        var top = ImportanceScorer.Rank(model, 3);
        if (top.Count > 0)
        {
            var names = top.Select(s => s.File.RelPath.Split('/')[^1]).ToList();
            clauses.Add($"the most central files are {JoinList(names)}");
        }

        var todos = model.Files.Sum(f => f.Todos.Count);
        if (todos > 0) { clauses.Add($"there are {todos:N0} open TODO/FIXME marker(s)"); }

        return Capitalize(string.Join("; ", clauses)) + ".";
    }

    /// <summary>One-sentence "what lives here" blurb for a top-level folder.</summary>
    public static string FolderBlurb(string folder, IReadOnlyList<FileNode> filesInFolder)
    {
        if (filesInFolder.Count == 0) { return ""; }
        var loc = filesInFolder.Sum(f => f.Loc);
        var topLang = filesInFolder
            .GroupBy(f => f.Language, StringComparer.Ordinal)
            .OrderByDescending(g => g.Sum(f => f.Loc)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .First().Key;
        var role = DominantRole(filesInFolder);
        var roleClause = role.Length > 0 ? $"; it looks like it handles {role}" : "";
        return $"{folder}/ holds {filesInFolder.Count:N0} file(s) ({loc:N0} lines), mostly {topLang}{roleClause}.";
    }

    private static string DominantRole(IReadOnlyList<FileNode> files)
    {
        // Group the heuristic purpose text into a role word; pick the most common.
        var roles = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var role = RoleWord(f.Purpose);
            if (role.Length > 0) { roles[role] = roles.GetValueOrDefault(role) + 1; }
        }
        if (roles.Count == 0) { return ""; }
        var best = roles.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        return best.Value >= 2 ? best.Key : "";
    }

    private static string RoleWord(string purpose)
    {
        var p = purpose.ToLowerInvariant();
        if (p.Contains("controller")) { return "HTTP controllers"; }
        if (p.Contains("service")) { return "services"; }
        if (p.Contains("repository")) { return "data access"; }
        if (p.Contains("test")) { return "tests"; }
        if (p.Contains("model")) { return "data models"; }
        if (p.Contains("page") || p.Contains("view")) { return "views/pages"; }
        if (p.Contains("config")) { return "configuration"; }
        return "";
    }

    private static List<string> TopExternalPackages(ProjectModel model, int take)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies)
        {
            if (e.ExternalTarget.Length > 0) { counts[e.ExternalTarget] = counts.GetValueOrDefault(e.ExternalTarget) + 1; }
        }
        return counts
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(take).Select(kv => kv.Key).ToList();
    }

    private static string TopFolder(FileNode f)
    {
        var idx = f.RelPath.IndexOf('/');
        return idx < 0 ? "(root)" : f.RelPath[..idx];
    }

    /// <summary>Joins a list with commas and a final "and"; never leaves a trailing conjunction.</summary>
    private static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        2 => $"{items[0]} and {items[1]}",
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
    };

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
