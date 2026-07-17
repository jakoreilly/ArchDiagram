using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site.Pages;

public static class DependenciesPage
{
    private const int MaxExternalNodes = 15;

    public static string Body(ProjectModel model, int maxNodes)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>File Dependencies</h1>");
        sb.Append("""
<p class="lede">How files connect to each other through imports (<code>using</code>, <code>import</code>,
<code>require</code>, dot-sourcing). Pick a folder to see the connections between its files, or the
cross-folder overview to see how whole areas of the codebase depend on each other. Grey dashed
nodes are external packages/modules that are imported but live outside this codebase.
Use the toggles to hide external packages or internal files, and the highlight box to spotlight
connections to a specific package (e.g. <code>system.data</code>).</p>
""");

        if (model.FileDependencies.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⇄</div><p>No imports between files were detected. " +
                      "Import analysis currently covers C#, TypeScript/JavaScript, Python and PowerShell sources.</p></div>");
            return sb.ToString();
        }

        // Dependency diagrams are a code-architecture view — test files/folders are excluded
        // (the viewer's 🧪 toggle governs HTML lists; these pre-rendered diagrams stay code-only).
        var folders = model.Files.Where(f => !f.IsTest).Select(TopFolder).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();

        sb.Append("<div class=\"select-row\"><label for=\"dep-select\">Show:</label><select id=\"dep-select\" data-diagram-select=\"deps\">");
        sb.Append("<option value=\"dep-overview\">Cross-folder overview</option>");
        foreach (var f in folders)
        {
            var id = "dep-" + Slugify(f);
            sb.Append($"<option value=\"{id}\">{Html.Encode(f)}/</option>");
        }
        sb.Append("</select></div>");
        sb.Append("""
<div class="landscape-filters" id="dep-filters" hidden>
  <label class="lf-check"><input type="checkbox" id="dep-internal" checked> Internal imports</label>
  <label class="lf-check"><input type="checkbox" id="dep-external" checked> External packages</label>
  <input type="search" class="filter-input" id="dep-filter" placeholder="Highlight… e.g. system.data" autocomplete="off" aria-label="Highlight connections matching text">
  <span id="dep-chips" class="dep-chips"></span>
  <span class="filter-count" id="dep-summary" style="margin-left:auto"></span>
</div>
""");
        if (GraphPage.HasData(model)) { sb.Append(PageTemplate.ExploreIn3DNote()); }

        sb.Append(PageTemplate.DiagramBlock("dep-overview", ArchitectureDiagrams.BuildFolderOverview(model, maxNodes), model.RootName + "-dependencies-overview", hidden: false, group: "deps"));
        foreach (var f in folders)
        {
            var id = "dep-" + Slugify(f);
            sb.Append(PageTemplate.DiagramBlock(id, BuildFolderDiagram(model, f, maxNodes), $"{model.RootName}-deps-{Slugify(f)}", hidden: true, group: "deps"));
        }
        sb.Append(PageTemplate.Legend());

        return sb.ToString();
    }

    private static string TopFolder(FileNode f)
    {
        var idx = f.RelPath.IndexOf('/');
        return idx < 0 ? "(root)" : f.RelPath[..idx];
    }

    private static string Slugify(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s) { sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_'); }
        return sb.ToString();
    }

    /// <summary>Files within one top-level folder, plus their most-imported external packages.</summary>
    public static Diagram BuildFolderDiagram(ProjectModel model, string folder, int maxNodes)
    {
        var files = model.Files.Where(f => !f.IsTest && TopFolder(f).Equals(folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var slugSet = new HashSet<string>(files.Select(f => f.Slug), StringComparer.Ordinal);
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);

        var nodes = new List<DiagramNode>();
        foreach (var f in files)
        {
            var name = f.RelPath.Split('/')[^1];
            nodes.Add(new DiagramNode("file:" + f.Slug, name, "file",
                Tooltip: $"{f.RelPath}\n{f.Language} · {f.Loc:N0} LOC\n{f.Purpose}",
                Href: $"files/{f.Slug}.html"));
        }

        var edges = new List<DiagramEdge>();
        var externalCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.Ordinal);
        foreach (var e in model.FileDependencies.Where(e => slugSet.Contains(e.FromSlug)))
        {
            if (e.ToSlug.Length > 0)
            {
                if (slugSet.Contains(e.ToSlug))
                {
                    edges.Add(new DiagramEdge("file:" + e.FromSlug, "file:" + e.ToSlug));
                }
                else if (bySlug.TryGetValue(e.ToSlug, out var other))
                {
                    // Cross-folder link: represent the other folder as one node.
                    var otherFolder = TopFolder(other);
                    var id = "xfold:" + otherFolder;
                    if (nodeIds.Add(id))
                    {
                        nodes.Add(new DiagramNode(id, otherFolder + "/", "folder", NodeShape.Rounded, $"Files in {otherFolder}/ (see that folder's diagram)"));
                    }
                    edges.Add(new DiagramEdge("file:" + e.FromSlug, id));
                }
            }
            else if (e.ExternalTarget.Length > 0)
            {
                externalCounts[e.ExternalTarget] = externalCounts.GetValueOrDefault(e.ExternalTarget) + 1;
            }
        }

        foreach (var (ext, count) in externalCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(MaxExternalNodes))
        {
            var id = "ext:" + ext;
            nodes.Add(new DiagramNode(id, ext, "external", NodeShape.Hexagon, $"External package/namespace: {ext}\nImported by {count} file(s) in {folder}/"));
            nodeIds.Add(id);
        }
        foreach (var e in model.FileDependencies.Where(e => slugSet.Contains(e.FromSlug) && e.ExternalTarget.Length > 0))
        {
            if (nodeIds.Contains("ext:" + e.ExternalTarget))
            {
                edges.Add(new DiagramEdge("file:" + e.FromSlug, "ext:" + e.ExternalTarget, "", Dashed: true));
            }
        }

        var (n2, e3) = GraphReducer.TrimToMax(nodes, edges.DistinctBy(x => (x.FromId, x.ToId)).ToList(), maxNodes);
        return MermaidRenderer.Render(n2, e3, totalNodes: nodes.Count);
    }
}
