using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site.Pages;

public static class TypesPage
{
    public const string EmptyStateCopy =
        "No C# sources were found in this folder, so type and call analysis is unavailable. " +
        "Structure and dependency pages cover all detected languages.";

    public static string Body(ProjectModel model)
    {
        return Body(model, 60);
    }

    public static string Body(ProjectModel model, int maxNodes)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Types &amp; Members</h1>");

        var typed = model.Files.Where(f => f.Types.Count > 0).ToList();
        if (typed.Count == 0)
        {
            sb.Append($"<div class=\"panel empty-state\"><div class=\"big\">❖</div><p>{Html.Encode(EmptyStateCopy)}</p></div>");
            return sb.ToString();
        }

        sb.Append("""
<p class="lede">Every C# type declared in the codebase, grouped by namespace, from syntax-only parsing
(no compilation required). Hover a method for its full signature; click a file link to open that
file's detail page.</p>
""");

        // Type-hierarchy diagram (inheritance / interface implementation) — only when there is
        // at least one edge between two declared types.
        var hierarchy = BuildHierarchyDiagram(model, maxNodes);
        if (hierarchy is not null)
        {
            sb.Append("<h2>Type hierarchy <span class=\"badge\">extends / implements</span></h2>");
            sb.Append("<p class=\"lede\">How declared classes and interfaces relate — an arrow points from a type to a "
                    + "base type or interface it declares. Only relationships between types defined in this codebase are shown.</p>");
            sb.Append(PageTemplate.DiagramBlock("type-hierarchy", hierarchy, model.RootName + "-type-hierarchy"));
            sb.Append(PageTemplate.Legend());
        }

        sb.Append("""
<div class="select-row"><input type="text" class="filter-input" data-filter-target=".ns-group" placeholder="Filter types, members, namespaces…" autocomplete="off" spellcheck="false"><span class="filter-count"></span></div>
""");

        var byNamespace = typed
            .SelectMany(f => f.Types.Select(t => (File: f, Type: t)))
            .GroupBy(x => x.Type.Namespace.Length > 0 ? x.Type.Namespace : "(global namespace)")
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var ns in byNamespace)
        {
            sb.Append("<section class=\"ns-group\">");
            sb.Append($"<h2><code>{Html.Encode(ns.Key)}</code> <span class=\"badge\">{ns.Count()} types</span></h2>");
            foreach (var (file, type) in ns.OrderBy(x => x.Type.Name, StringComparer.Ordinal))
            {
                var searchText = $"{ns.Key} {type.Name} {type.Kind} {string.Join(' ', type.Methods.Select(m => m.Name))}";
                var testAttr = file.IsTest ? " data-test=\"1\"" : "";
                sb.Append($"<div class=\"type-card filterable\"{testAttr} data-search=\"{Html.Encode(searchText.ToLowerInvariant())}\">");
                sb.Append("<div class=\"type-head\">");
                sb.Append($"<span class=\"badge accent\">{Html.Encode(type.Kind)}</span>");
                sb.Append($"<span class=\"type-name\">{Html.Encode(type.Name)}</span>");
                if (type.BaseTypes.Count > 0) { sb.Append($"<span class=\"badge\" title=\"Base types / implemented interfaces\">: {Html.Encode(string.Join(", ", type.BaseTypes))}</span>"); }
                sb.Append($"<a href=\"files/{file.Slug}.html\" style=\"margin-left:auto;font-size:.82rem\" title=\"{Html.Encode(file.RelPath)}\">{Html.Encode(file.RelPath)}</a>");
                sb.Append("</div>");
                if (type.XmlSummary.Length > 0) { sb.Append($"<p class=\"lede\" style=\"margin:.4rem 0 0;font-size:.88rem\">{Html.Encode(type.XmlSummary)}</p>"); }
                if (type.Properties.Count > 0 || type.Fields.Count > 0)
                {
                    var shape = type.Properties.Concat(type.Fields).OrderBy(s => s, StringComparer.Ordinal);
                    sb.Append($"<div style=\"margin:.4rem 0 0;font-size:.82rem;color:var(--text-soft)\"><strong>Data:</strong> " +
                              $"{Html.Encode(string.Join(", ", shape.Take(20)))}" +
                              (type.Properties.Count + type.Fields.Count > 20 ? ", …" : "") + "</div>");
                }
                if (type.Methods.Count > 0)
                {
                    sb.Append("<ul class=\"member-list\">");
                    foreach (var m in type.Methods.OrderBy(m => m.Name, StringComparer.Ordinal))
                    {
                        var summary = m.XmlSummary.Length > 0 ? $"<span class=\"member-summary\">— {Html.Encode(m.XmlSummary)}</span>" : "";
                        sb.Append($"<li title=\"{Html.Encode(m.Signature)}\">{Html.Encode(m.Signature)}{summary}</li>");
                    }
                    sb.Append("</ul>");
                }
                sb.Append("</div>");
            }
            sb.Append("</section>");
        }

        return sb.ToString();
    }

    /// <summary>Inheritance/implementation diagram among declared types (child → base), keeping
    /// only edges whose base type is also declared in this codebase. Null when there are none.</summary>
    private static Diagram? BuildHierarchyDiagram(ProjectModel model, int maxNodes)
    {
        var typesByName = new Dictionary<string, (FileNode File, TypeInfo Type)>(StringComparer.Ordinal);
        foreach (var f in model.Files)
        {
            foreach (var t in f.Types)
            {
                typesByName.TryAdd(t.Name, (f, t));
            }
        }

        var edges = new List<DiagramEdge>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in model.Files)
        {
            foreach (var t in f.Types)
            {
                foreach (var baseRaw in t.BaseTypes)
                {
                    var baseName = SimpleName(baseRaw);
                    if (baseName == t.Name || !typesByName.ContainsKey(baseName)) { continue; }
                    edges.Add(new DiagramEdge("ty:" + t.Name, "ty:" + baseName));
                    used.Add(t.Name);
                    used.Add(baseName);
                }
            }
        }
        if (edges.Count == 0) { return null; }

        var nodes = used
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n =>
            {
                var (file, type) = typesByName[n];
                var css = type.Kind == "interface" ? "external" : "typenode";
                return new DiagramNode("ty:" + n, n, css,
                    Tooltip: $"{type.Kind} {n}\n{file.RelPath}", Href: $"files/{file.Slug}.html");
            })
            .ToList();

        var (dn, de) = GraphReducer.TrimToMax(nodes, edges.DistinctBy(e => (e.FromId, e.ToId)).ToList(), maxNodes);
        return MermaidRenderer.Render(dn, de, direction: "BT", totalNodes: nodes.Count);
    }

    /// <summary>Simple type name: strips namespace qualifier and generic arguments
    /// (<c>Foo.Bar&lt;T&gt;</c> → <c>Bar</c>).</summary>
    private static string SimpleName(string typeRef)
    {
        var s = typeRef;
        var lt = s.IndexOf('<');
        if (lt >= 0) { s = s[..lt]; }
        var dot = s.LastIndexOf('.');
        if (dot >= 0) { s = s[(dot + 1)..]; }
        return s.Trim();
    }
}
