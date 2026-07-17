using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site;

/// <summary>The two whole-project architecture diagrams (projects+databases, and the
/// folder-level fallback for repos with no .csproj) — shared by the interactive Overview/
/// Dependencies pages (ArchDiagram.Site.Pages) AND the Markdown/wiki exporters
/// (ArchDiagram.Site), which is exactly why this lives in ArchDiagram.Site rather than
/// ArchDiagram.Site.Pages: putting it in Pages would make the exporters (a lower/sibling
/// layer) depend upward into Pages, the same against-the-grain shape Phase 3 removed from
/// SiteGenerator. TreemapRenderer.cs is the existing precedent for a diagram builder living
/// here for the same reason.</summary>
public static class ArchitectureDiagrams
{
    public static Diagram BuildProjectDiagram(ProjectModel model, int maxNodes)
    {
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();

        foreach (var p in model.Projects)
        {
            var packages = p.PackageReferences.Count == 0 ? "none"
                : string.Join(", ", p.PackageReferences.Take(6)) + (p.PackageReferences.Count > 6 ? ", …" : "");
            var tooltip = $"{p.RelPath}\nTarget: {(p.TargetFramework.Length > 0 ? p.TargetFramework : "unknown")}\nPackages: {packages}";
            nodes.Add(new DiagramNode("proj:" + p.Name, p.Name, "service", Tooltip: tooltip));
        }

        var projNames = new HashSet<string>(model.Projects.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var p in model.Projects)
        {
            foreach (var r in p.ProjectReferenceNames.Where(projNames.Contains))
            {
                edges.Add(new DiagramEdge("proj:" + p.Name, "proj:" + r, "reference"));
            }
        }

        foreach (var db in model.Databases)
        {
            var tooltip = $"Database node (deduplicated by normalized connection string)\nServer: {(db.Server.Length > 0 ? db.Server : "unknown")}\nCatalog: {(db.Catalog.Length > 0 ? db.Catalog : "unknown")}\nHash: {db.Hash[..16]}…";
            nodes.Add(new DiagramNode("db:" + db.Hash, db.Label, "database", NodeShape.Database, tooltip));
        }
        foreach (var p in model.Projects)
        {
            foreach (var hash in p.ConnectionStrings.Select(c => c.Hash).Distinct())
            {
                edges.Add(new DiagramEdge("proj:" + p.Name, "db:" + hash, "sql"));
            }
        }

        var (n, e) = GraphReducer.TrimToMax(nodes, edges.DistinctBy(x => (x.FromId, x.ToId, x.Label)).ToList(), maxNodes);
        return MermaidRenderer.Render(n, e, totalNodes: nodes.Count);
    }

    /// <summary>Top-level folders as nodes; edges aggregate the file-level imports crossing them.</summary>
    public static Diagram BuildFolderOverview(ProjectModel model, int maxNodes)
    {
        var codeFiles = model.Files.Where(f => !f.IsTest).ToList();
        var folderOf = codeFiles.ToDictionary(f => f.Slug, TopFolder, StringComparer.Ordinal);
        var fileCount = codeFiles.GroupBy(TopFolder, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var counts = new Dictionary<(string From, string To), int>();
        foreach (var e in model.FileDependencies.Where(e => e.ToSlug.Length > 0))
        {
            // Skip edges touching an excluded (test) file.
            if (!folderOf.TryGetValue(e.FromSlug, out var from) || !folderOf.TryGetValue(e.ToSlug, out var to)) { continue; }
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) { continue; }
            counts[(from, to)] = counts.GetValueOrDefault((from, to)) + 1;
        }

        var involved = counts.Keys.SelectMany(k => new[] { k.From, k.To }).Distinct(StringComparer.OrdinalIgnoreCase);
        var nodes = fileCount.Keys.Union(involved, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new DiagramNode("fold:" + f, f + "/", "folder", NodeShape.Rounded,
                Tooltip: $"Folder: {f}/\nFiles: {fileCount.GetValueOrDefault(f, 0):N0}"))
            .ToList();
        var edges = counts.OrderBy(kv => kv.Key.From, StringComparer.OrdinalIgnoreCase).ThenBy(kv => kv.Key.To, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new DiagramEdge("fold:" + kv.Key.From, "fold:" + kv.Key.To, kv.Value == 1 ? "" : $"{kv.Value} imports"))
            .ToList();

        var (n, e2) = GraphReducer.TrimToMax(nodes, edges, maxNodes);
        return MermaidRenderer.Render(n, e2, totalNodes: nodes.Count);
    }

    // Duplicated from DependenciesPage.TopFolder (private there) rather than referenced —
    // this file must not depend on ArchDiagram.Site.Pages at all (see the class doc comment).
    private static string TopFolder(FileNode f)
    {
        var idx = f.RelPath.IndexOf('/');
        return idx < 0 ? "(root)" : f.RelPath[..idx];
    }
}
