using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site.Pages;

public static class FilePage
{
    public static string Body(ProjectModel model, FileNode file, int maxNodes,
        bool showComplexity = false, bool showSnippets = false) =>
        Body(SiteContext.Build(model), file, maxNodes, showComplexity, showSnippets);

    /// <summary>Reuses the site-wide slug/edge indexes in <paramref name="ctx"/> instead of
    /// scanning model.FileDependencies/Calls from scratch for every file page.</summary>
    public static string Body(SiteContext ctx, FileNode file, int maxNodes,
        bool showComplexity = false, bool showSnippets = false)
    {
        var model = ctx.Model;
        var bySlug = ctx.BySlug;
        var incoming = ctx.IncomingDeps.GetValueOrDefault(file.Slug) ?? [];
        var outgoing = ctx.OutgoingDeps.GetValueOrDefault(file.Slug) ?? [];
        var callsOut = ctx.CallsOut.GetValueOrDefault(file.Slug) ?? [];
        var callsIn = ctx.CallsIn.GetValueOrDefault(file.Slug) ?? [];

        var sb = new StringBuilder();
        var name = file.RelPath.Split('/')[^1];
        sb.Append($"<h1>{Html.Encode(name)}</h1>");
        sb.Append($"<p class=\"lede\"><code>{Html.Encode(file.RelPath)}</code></p>");

        // Source link (resolved client-side by sourcelink.js — works whether the
        // base was configured at generation time or entered via the browser prompt).
        sb.Append($"<p class=\"source-link\" data-sourcelink-path=\"{Html.Encode(file.RelPath)}\"></p>");

        // Deep-link into the 3D graph, pre-focused on this file (graph3d.js reads #path=).
        if (GraphPage.HasData(model))
        {
            sb.Append($"<p><a class=\"btn\" href=\"../graph.html#path={Uri.EscapeDataString(file.RelPath)}\">View in 3D graph →</a></p>");
        }

        AppendTiles(sb, model, file, incoming, outgoing);
        AppendPurpose(sb, file);
        if (incoming.Count > 0 || outgoing.Count > 0) { AppendConnections(sb, file, incoming, outgoing, bySlug, maxNodes); }
        if (file.Imports.Count > 0) { AppendImports(sb, file); }
        if (file.Todos.Count > 0) { AppendTodos(sb, file); }
        if (file.Types.Count > 0) { AppendTypesAndMethods(sb, model, file, showComplexity, showSnippets); }
        if (callsOut.Count > 0 || callsIn.Count > 0) { AppendCrossFileCalls(sb, callsOut, callsIn, bySlug); }

        return sb.ToString();
    }

    private static void AppendTiles(StringBuilder sb, ProjectModel model, FileNode file, List<DepEdge> incoming, List<DepEdge> outgoing)
    {
        sb.Append("<div class=\"tiles\">");
        var owningProject = OwningProject(model, file.RelPath);
        if (owningProject.Length > 0)
        {
            sb.Append($"<div class=\"tile\"><div class=\"num\" style=\"font-size:1.1rem\">{Html.Encode(owningProject)}</div><div class=\"lbl\">Project</div></div>");
        }
        sb.Append($"<div class=\"tile\"><div class=\"num\">{Html.Encode(file.Language)}</div><div class=\"lbl\">Language</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{file.Loc:N0}</div><div class=\"lbl\">Lines</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{StructurePage.FormatBytes(file.SizeBytes)}</div><div class=\"lbl\">Size</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{file.Types.Count:N0}</div><div class=\"lbl\">Types</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{incoming.Count + outgoing.Count:N0}</div><div class=\"lbl\">File links</div></div>");
        sb.Append("</div>");
    }

    private static void AppendPurpose(StringBuilder sb, FileNode file)
    {
        if (file.Purpose.Length == 0) { return; }
        var badge = file.PurposeSource == "authored"
            ? "<span class=\"badge accent\" title=\"Hand-written in the descriptions sidecar\">authored</span>"
            : $"<span class=\"badge warn\" title=\"Derived automatically — not hand-written documentation\">heuristic · {Html.Encode(file.PurposeSource)}</span>";
        sb.Append($"<div class=\"panel\"><strong>Purpose</strong> {badge}<p style=\"margin:.4rem 0 0\">{Html.Encode(file.Purpose)}</p></div>");
    }

    // Mini dependency diagram: this file + direct neighbours.
    private static void AppendConnections(StringBuilder sb, FileNode file, List<DepEdge> incoming, List<DepEdge> outgoing,
        IReadOnlyDictionary<string, FileNode> bySlug, int maxNodes)
    {
        sb.Append("<h2>Connections</h2>");
        sb.Append("<p class=\"lede\">Files that import this one (left) and everything this file imports (right). Grey dashed nodes are external packages.</p>");
        var block = PageTemplate.DiagramBlock("filedeps", BuildNeighbourDiagram(file, incoming, outgoing, bySlug, maxNodes), file.Slug + "-connections");
        sb.Append(block.Replace("class=\"stage\"", "class=\"stage small\""));
        sb.Append(PageTemplate.Legend());
    }

    private static void AppendImports(StringBuilder sb, FileNode file)
    {
        sb.Append($"<h2>Imports <span class=\"badge\">{file.Imports.Count}</span></h2><div class=\"panel\"><ul class=\"member-list\">");
        foreach (var i in file.Imports) { sb.Append($"<li>{Html.Encode(i)}</li>"); }
        sb.Append("</ul></div>");
    }

    private static void AppendTodos(StringBuilder sb, FileNode file)
    {
        sb.Append($"<h2>Open markers <span class=\"badge warn\">{file.Todos.Count}</span></h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>Tag</th><th>Line</th><th>Text</th></tr></thead><tbody>");
        foreach (var t in file.Todos)
        {
            var cls = t.Tag is "FIXME" or "BUG" ? "warn" : "";
            var attribution = t.Author.Length > 0 ? $" <span class=\"badge accent\">{Html.Encode(t.Author)}</span>" : "";
            sb.Append($"<tr><td><span class=\"badge {cls}\">{Html.Encode(t.Tag)}</span></td><td>{t.Line}</td><td>{Html.Encode(t.Text)}{attribution}</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendTypesAndMethods(StringBuilder sb, ProjectModel model, FileNode file, bool showComplexity, bool showSnippets)
    {
        // Read the source once per page only when snippets are needed; skipped
        // silently if the file moved or is unreadable (snippets just won't show).
        var wantsSnippets = showSnippets && file.Types.Any(t => t.Methods.Any(m =>
            m.Cognitive >= Severity.HighThreshold && m.StartLine > 0 && m.EndLine >= m.StartLine));
        var sourceLines = wantsSnippets ? TryReadLines(model.SourcePath, file.RelPath) : null;

        sb.Append("<h2>Types &amp; methods</h2>");
        foreach (var type in file.Types)
        {
            sb.Append("<div class=\"type-card\"><div class=\"type-head\">");
            sb.Append($"<span class=\"badge accent\">{Html.Encode(type.Kind)}</span><span class=\"type-name\">{Html.Encode(type.Name)}</span>");
            if (type.Namespace.Length > 0) { sb.Append($"<span class=\"badge\" title=\"Namespace\">{Html.Encode(type.Namespace)}</span>"); }
            if (type.BaseTypes.Count > 0) { sb.Append($"<span class=\"badge\" title=\"Base types / implemented interfaces\">: {Html.Encode(string.Join(", ", type.BaseTypes))}</span>"); }
            sb.Append("</div>");
            if (type.XmlSummary.Length > 0) { sb.Append($"<p class=\"lede\" style=\"margin:.4rem 0 0;font-size:.88rem\">{Html.Encode(type.XmlSummary)}</p>"); }
            if (type.Methods.Count > 0)
            {
                sb.Append("<ul class=\"member-list\">");
                foreach (var m in type.Methods)
                {
                    var summary = m.XmlSummary.Length > 0 ? $"<span class=\"member-summary\">— {Html.Encode(m.XmlSummary)}</span>" : "";
                    var badges = showComplexity ? ComplexityBadges(m) : "";
                    var srcLink = m.StartLine > 0
                        ? $"<span class=\"source-link\" data-sourcelink-path=\"{Html.Encode(file.RelPath)}\" data-sourcelink-line=\"{m.StartLine}\" data-sourcelink-mini></span>"
                        : "";
                    sb.Append($"<li title=\"{Html.Encode(m.Signature)}\">{Html.Encode(m.Signature)}{badges} {srcLink}{summary}");
                    if (showSnippets) { AppendSnippet(sb, m, sourceLines); }
                    sb.Append("</li>");
                }
                sb.Append("</ul>");
            }
            sb.Append("</div>");
        }
    }

    private static void AppendCrossFileCalls(StringBuilder sb, List<CallEdge> callsOut, List<CallEdge> callsIn, IReadOnlyDictionary<string, FileNode> bySlug)
    {
        sb.Append("<h2>Cross-file calls <span class=\"badge warn\" title=\"Matched by method name and parameter count — no compiler symbol resolution\">heuristic</span></h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>Direction</th><th>Method here</th><th>Other method</th><th>Other file</th><th></th></tr></thead><tbody>");
        foreach (var c in callsOut.Take(40))
        {
            var other = bySlug.GetValueOrDefault(c.CalleeSlug);
            sb.Append($"<tr><td>→ calls</td><td><code>{Html.Encode(c.CallerType)}.{Html.Encode(c.CallerMethod)}</code></td>" +
                      $"<td><code>{Html.Encode(c.CalleeType)}.{Html.Encode(c.CalleeMethod)}</code></td>" +
                      $"<td>{FileLink(other)}</td><td>{(c.Ambiguous ? "<span class=\"badge warn\" title=\"Several declared methods matched this call\">ambiguous</span>" : "")}</td></tr>");
        }
        foreach (var c in callsIn.Take(40))
        {
            var other = bySlug.GetValueOrDefault(c.CallerSlug);
            sb.Append($"<tr><td>← called by</td><td><code>{Html.Encode(c.CalleeType)}.{Html.Encode(c.CalleeMethod)}</code></td>" +
                      $"<td><code>{Html.Encode(c.CallerType)}.{Html.Encode(c.CallerMethod)}</code></td>" +
                      $"<td>{FileLink(other)}</td><td>{(c.Ambiguous ? "<span class=\"badge warn\" title=\"Several declared methods matched this call\">ambiguous</span>" : "")}</td></tr>");
        }
        sb.Append("</tbody></table>");
        var hidden = Math.Max(0, callsOut.Count - 40) + Math.Max(0, callsIn.Count - 40);
        if (hidden > 0) { sb.Append($"<p class=\"note\">{hidden} more call links omitted for brevity — see model.json for the full list.</p>"); }
    }

    /// <summary>Complexity badges for a method: a severity band on cognitive plus a
    /// plain cyclomatic count. Trivial members (no branching) get nothing.</summary>
    private static string ComplexityBadges(MethodInfo m)
    {
        if (m.Cyclomatic <= 1 && m.Cognitive == 0) { return ""; }
        var title = $" title=\"Cognitive complexity {m.Cognitive}; cyclomatic {m.Cyclomatic}\"";
        return $"<span{title}>{Severity.Badge(m.Cognitive)}</span>" +
               $"<span class=\"badge\"{title}>CC {m.Cyclomatic}</span>";
    }

    private const int MaxSnippetLines = 200;

    /// <summary>Collapsible source of a High/Very-High complexity method, sliced from
    /// the file's lines. Silently omitted if the source is missing or the span invalid.</summary>
    private static void AppendSnippet(StringBuilder sb, MethodInfo m, string[]? sourceLines)
    {
        if (sourceLines is null || m.Cognitive < Severity.HighThreshold) { return; }
        if (m.StartLine <= 0 || m.EndLine < m.StartLine || m.StartLine > sourceLines.Length) { return; }

        var start = m.StartLine - 1;                    // to 0-based
        var end = Math.Min(m.EndLine, sourceLines.Length); // 1-based inclusive
        var truncated = end - start > MaxSnippetLines;
        if (truncated) { end = start + MaxSnippetLines; }

        var code = string.Join("\n", sourceLines[start..end]);
        sb.Append($"<details class=\"code-snippet\"><summary>Show source · lines {m.StartLine}–{m.EndLine}</summary>");
        sb.Append($"<pre><code>{Html.Encode(code)}</code></pre>");
        if (truncated) { sb.Append("<p class=\"note\">… truncated; open the file for the rest.</p>"); }
        sb.Append("</details>");
    }

    private static string[]? TryReadLines(string sourceRoot, string relPath)
    {
        try { return File.ReadAllLines(Path.Combine(sourceRoot, relPath.Replace('/', Path.DirectorySeparatorChar))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>Name of the .NET project whose folder most closely encloses this file, or "".</summary>
    private static string OwningProject(ProjectModel model, string relPath)
    {
        var best = "";
        var bestLen = -1;
        foreach (var p in model.Projects)
        {
            var idx = p.RelPath.LastIndexOf('/');
            var dir = idx < 0 ? "" : p.RelPath[..idx];
            var underDir = dir.Length == 0 || relPath.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase);
            if (underDir && dir.Length > bestLen) { best = p.Name; bestLen = dir.Length; }
        }
        return best;
    }

    private static string FileLink(FileNode? f) =>
        f is null ? "" : $"<a href=\"{f.Slug}.html\" title=\"{Html.Encode(f.Purpose)}\">{Html.Encode(f.RelPath)}</a>";

    private static Diagram BuildNeighbourDiagram(FileNode file, List<DepEdge> incoming, List<DepEdge> outgoing,
        IReadOnlyDictionary<string, FileNode> bySlug, int maxNodes)
    {
        var nodes = new List<DiagramNode>
        {
            new("this", file.RelPath.Split('/')[^1], "typenode", Tooltip: $"{file.RelPath}\n(this page)"),
        };
        var edges = new List<DiagramEdge>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { "this" };

        void AddFile(string slug, out string id)
        {
            id = "f:" + slug;
            if (!seen.Add(id)) { return; }
            var f = bySlug.GetValueOrDefault(slug);
            nodes.Add(new DiagramNode(id, f?.RelPath.Split('/')[^1] ?? slug, "file",
                Tooltip: f is null ? "" : $"{f.RelPath}\n{f.Purpose}",
                Href: f is null ? "" : $"{f.Slug}.html"));
        }

        foreach (var e in incoming)
        {
            AddFile(e.FromSlug, out var id);
            edges.Add(new DiagramEdge(id, "this"));
        }
        foreach (var e in outgoing)
        {
            if (e.ToSlug.Length > 0)
            {
                AddFile(e.ToSlug, out var id);
                edges.Add(new DiagramEdge("this", id));
            }
            else if (e.ExternalTarget.Length > 0)
            {
                var id = "ext:" + e.ExternalTarget;
                if (seen.Add(id)) { nodes.Add(new DiagramNode(id, e.ExternalTarget, "external", NodeShape.Hexagon, $"External package/namespace: {e.ExternalTarget}")); }
                edges.Add(new DiagramEdge("this", id, "", Dashed: true));
            }
        }

        var (n, e2) = GraphReducer.TrimToMax(nodes, edges.DistinctBy(x => (x.FromId, x.ToId)).ToList(), maxNodes);
        return MermaidRenderer.Render(n, e2, totalNodes: nodes.Count);
    }
}
