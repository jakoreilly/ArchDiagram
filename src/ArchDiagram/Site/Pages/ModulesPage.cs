using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site.Pages;

/// <summary>The module-level view: files grouped into modules (namespace or top-level
/// folder), a module-to-module dependency diagram, and a coupling matrix. Sits between the
/// whole-project Overview and the file-by-file Dependencies page.</summary>
public static class ModulesPage
{
    private const int MaxMatrixModules = 15;

    public static string Body(ProjectModel model, int maxNodes)
    {
        var graph = ModuleGrouper.Build(model);
        var sb = new StringBuilder();
        sb.Append("<h1>Modules</h1>");

        var by = graph.Mode == "namespace" ? "C# namespace" : "top-level folder";
        sb.Append($"<p class=\"lede\">Files grouped into modules by {by}, and how those modules depend on each "
                + "other. This is the mid-level view between the whole-project Overview and the file-by-file "
                + "Dependencies page. Module coupling is aggregated from the imports between the files inside them.</p>");

        if (graph.Modules.Count < 2 || graph.CrossModuleLinks == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⬡</div>" +
                      "<p>This codebase resolves to a single module (or has no cross-module imports), so there is "
                      + "no module-level dependency to show. See the <a href=\"dependencies.html\">Dependencies</a> "
                      + "page for file-level links.</p></div>");
            return sb.ToString();
        }

        var mostCoupled = graph.Modules
            .OrderByDescending(m => graph.Edges.Where(e => e.Key.From == m.Key || e.Key.To == m.Key).Sum(e => e.Value))
            .ThenBy(m => m.Key, StringComparer.Ordinal)
            .First();

        sb.Append("<div class=\"tiles\">");
        Tile(sb, graph.Modules.Count.ToString("N0"), "Modules");
        Tile(sb, graph.CrossModuleLinks.ToString("N0"), "Cross-module links");
        Tile(sb, Html.Encode(mostCoupled.Key), "Most-coupled module");
        sb.Append("</div>");

        // Module -> module diagram.
        sb.Append(PageTemplate.DiagramBlock("modules", BuildDiagram(graph, maxNodes), model.RootName + "-modules"));
        sb.Append(PageTemplate.Legend());

        // Coupling matrix (top modules by file count).
        AppendMatrix(sb, graph);

        return sb.ToString();
    }

    private static Diagram BuildDiagram(ModuleGrouper.ModuleGraph graph, int maxNodes)
    {
        var nodes = graph.Modules
            .Select(m => new DiagramNode("mod:" + m.Key, m.Key, "folder", NodeShape.Rounded,
                Tooltip: $"Module: {m.Key}\nFiles: {m.FileCount:N0} · {m.Loc:N0} LOC"))
            .ToList();
        var edges = graph.Edges
            .OrderBy(kv => kv.Key.From, StringComparer.Ordinal).ThenBy(kv => kv.Key.To, StringComparer.Ordinal)
            .Select(kv => new DiagramEdge("mod:" + kv.Key.From, "mod:" + kv.Key.To, kv.Value == 1 ? "" : $"{kv.Value} imports"))
            .ToList();

        var (n, e) = GraphReducer.TrimToMax(nodes, edges, maxNodes);
        return MermaidRenderer.Render(n, e, totalNodes: nodes.Count);
    }

    private static void AppendMatrix(StringBuilder sb, ModuleGrouper.ModuleGraph graph)
    {
        var shown = graph.Modules
            .OrderByDescending(m => m.FileCount).ThenBy(m => m.Key, StringComparer.Ordinal)
            .Take(MaxMatrixModules)
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToList();

        // Number the modules so the axes stay narrow; the legend maps number → full name.
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < shown.Count; i++) { index[shown[i].Key] = i + 1; }
        var max = graph.Edges.Count == 0 ? 1 : Math.Max(1, graph.Edges.Values.Max());

        // Strip the dotted/slashed prefix common to every real module so long namespaces
        // will be read as their distinctive tail. Pseudo
        // buckets like "(no namespace)" are ignored so they don't defeat the shared prefix.
        var commonPrefix = CommonPrefix(shown.Select(m => m.Key).Where(k => !k.StartsWith('(')).ToList());

        sb.Append("<h2>Coupling matrix <span class=\"badge\">imports: row → column</span></h2>");
        sb.Append("<p class=\"lede\">Each cell counts imports from the row module into the column module — "
                + "darker means more. Read across a row to see what a module depends on; read down a column to "
                + "see what depends on it. Modules are numbered; the legend maps each number to its name.</p>");
        if (commonPrefix.Length > 0)
        {
            sb.Append($"<p class=\"note\">All modules share the prefix <code>{Html.Encode(commonPrefix)}</code> — it is omitted from the names below.</p>");
        }

        // Legend: a plain, wrapping table (number → module name → file count).
        sb.Append("<table class=\"grid matrix-legend\"><thead><tr><th>#</th><th>Module</th><th>Files</th></tr></thead><tbody>");
        foreach (var m in shown)
        {
            var name = commonPrefix.Length > 0 && m.Key.StartsWith(commonPrefix, StringComparison.Ordinal) && m.Key.Length > commonPrefix.Length
                ? m.Key[commonPrefix.Length..] : m.Key;
            sb.Append($"<tr><td class=\"idx\">{index[m.Key]}</td><td>{Html.Encode(name)}</td><td>{m.FileCount:N0}</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<div style=\"overflow-x:auto\"><table class=\"grid matrix\"><thead><tr><th title=\"from ↓ / to →\">↓ / →</th>");
        foreach (var m in shown) { sb.Append($"<th title=\"{Html.Encode(m.Key)}\">{index[m.Key]}</th>"); }
        sb.Append("</tr></thead><tbody>");
        foreach (var row in shown)
        {
            sb.Append($"<tr><th title=\"{Html.Encode(row.Key)}\">{index[row.Key]}</th>");
            foreach (var col in shown)
            {
                if (row.Key == col.Key) { sb.Append("<td class=\"zero\">·</td>"); continue; }
                var n = graph.Edges.GetValueOrDefault((row.Key, col.Key));
                if (n == 0) { sb.Append("<td class=\"zero\"></td>"); continue; }
                var pct = 12 + (int)(63.0 * n / max);   // deterministic heatmap intensity
                sb.Append($"<td style=\"background:color-mix(in srgb, var(--accent) {pct}%, transparent)\" title=\"{Html.Encode(row.Key)} → {Html.Encode(col.Key)}: {n}\">{n:N0}</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></div>");

        if (graph.Modules.Count > MaxMatrixModules)
        {
            sb.Append($"<p class=\"note\">{graph.Modules.Count - MaxMatrixModules} smaller module(s) omitted from the matrix — see the diagram above and model.json.</p>");
        }
    }

    /// <summary>Longest leading run shared by all keys, trimmed back to the last '.' or '/'
    /// boundary so a partial segment is never stripped. Empty when there is no shared prefix.</summary>
    private static string CommonPrefix(IReadOnlyList<string> keys)
    {
        if (keys.Count < 2) { return ""; }
        var prefix = keys[0];
        foreach (var k in keys.Skip(1))
        {
            var n = Math.Min(prefix.Length, k.Length);
            var i = 0;
            while (i < n && prefix[i] == k[i]) { i++; }
            prefix = prefix[..i];
            if (prefix.Length == 0) { return ""; }
        }
        var boundary = prefix.LastIndexOfAny(['.', '/']);
        return boundary <= 0 ? "" : prefix[..(boundary + 1)];
    }

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
}
