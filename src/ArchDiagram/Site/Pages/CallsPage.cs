using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site.Pages;

public static class CallsPage
{
    public static string Body(ProjectModel model, int maxNodes)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Method Call Graph</h1>");

        if (model.Files.All(f => f.Types.Count == 0))
        {
            sb.Append($"<div class=\"panel empty-state\"><div class=\"big\">☎</div><p>{Html.Encode(TypesPage.EmptyStateCopy)}</p></div>");
            return sb.ToString();
        }

        // Test files are hidden by default in the viewer, but this diagram is pre-rendered SVG the
        // CSS toggle can't reach — so exclude any call touching a test file at generation time,
        // consistent with the treemap and the Start-here ranking.
        var testSlugs = new HashSet<string>(model.Files.Where(f => f.IsTest).Select(f => f.Slug), StringComparer.Ordinal);
        var calls = model.Calls.Where(e => !testSlugs.Contains(e.CallerSlug) && !testSlugs.Contains(e.CalleeSlug)).ToList();

        if (calls.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">☎</div><p>No cross-file method calls were detected between the declared (non-test) C# methods.</p></div>");
            return sb.ToString();
        }

        sb.Append("""
<p class="lede">Method-to-method connections across files, scoped to one type at a time (a whole-project
call graph would be unreadable). Pick a type to see what its methods call and what calls into them.</p>
<p class="note"><strong>How this is derived:</strong> calls are matched heuristically by method name and
parameter count from syntax-only parsing — there is no compiler symbol resolution. A <em>dashed</em> arrow
means several declared methods matched (ambiguous); very common names like <code>ToString</code> or
<code>Add</code> are only linked when exactly one matching declaration exists in this codebase.
Test files are excluded from this graph.</p>
""");

        // Types that participate in at least one edge, ordered by participation.
        var participation = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in calls)
        {
            participation[e.CallerType] = participation.GetValueOrDefault(e.CallerType) + 1;
            participation[e.CalleeType] = participation.GetValueOrDefault(e.CalleeType) + 1;
        }
        var types = participation.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key).ToList();

        sb.Append("<div class=\"select-row\"><label for=\"call-select\">Type:</label><select id=\"call-select\" data-diagram-select=\"calls\">");
        foreach (var t in types)
        {
            sb.Append($"<option value=\"call-{Slug(t)}\">{Html.Encode(t)} ({participation[t]} links)</option>");
        }
        sb.Append("</select></div>");
        if (GraphPage.HasData(model)) { sb.Append(PageTemplate.ExploreIn3DNote()); }

        var shown = true;
        foreach (var t in types)
        {
            sb.Append(PageTemplate.DiagramBlock("call-" + Slug(t), BuildTypeDiagram(model, calls, t, maxNodes),
                $"{model.RootName}-calls-{Slug(t)}", hidden: !shown, group: "calls"));
            shown = false;
        }
        sb.Append(PageTemplate.Legend());

        return sb.ToString();
    }

    private static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s) { sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_'); }
        return sb.ToString();
    }

    public static Diagram BuildTypeDiagram(ProjectModel model, IReadOnlyList<CallEdge> calls, string typeName, int maxNodes)
    {
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);
        var edges = calls.Where(e => e.CallerType == typeName || e.CalleeType == typeName).ToList();

        var nodes = new List<DiagramNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddNode(string slug, string type, string method)
        {
            var id = $"m:{slug}:{type}.{method}";
            if (!seen.Add(id)) { return; }
            var rel = bySlug.TryGetValue(slug, out var f) ? f.RelPath : "";
            var css = type == typeName ? "typenode" : "file";
            nodes.Add(new DiagramNode(id, $"{type}.{method}", css,
                Tooltip: $"{type}.{method}\nDeclared in: {rel}",
                Href: rel.Length == 0 ? "" : $"files/{slug}.html"));
        }

        foreach (var e in edges)
        {
            AddNode(e.CallerSlug, e.CallerType, e.CallerMethod);
            AddNode(e.CalleeSlug, e.CalleeType, e.CalleeMethod);
        }

        var dEdges = edges
            .Select(e => new DiagramEdge($"m:{e.CallerSlug}:{e.CallerType}.{e.CallerMethod}", $"m:{e.CalleeSlug}:{e.CalleeType}.{e.CalleeMethod}", "", e.Ambiguous))
            .DistinctBy(x => (x.FromId, x.ToId))
            .ToList();

        var (n, e2) = GraphReducer.TrimToMax(nodes, dEdges, maxNodes);
        return MermaidRenderer.Render(n, e2, totalNodes: nodes.Count);
    }
}
