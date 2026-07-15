using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

public static class TypesPage
{
    public const string EmptyStateCopy =
        "No C# sources were found in this folder, so type and call analysis is unavailable. " +
        "Structure and dependency pages cover all detected languages.";

    public static string Body(ProjectModel model)
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
                sb.Append($"<div class=\"type-card filterable\" data-search=\"{Html.Encode(searchText.ToLowerInvariant())}\">");
                sb.Append("<div class=\"type-head\">");
                sb.Append($"<span class=\"badge accent\">{Html.Encode(type.Kind)}</span>");
                sb.Append($"<span class=\"type-name\">{Html.Encode(type.Name)}</span>");
                if (type.BaseTypes.Count > 0) { sb.Append($"<span class=\"badge\" title=\"Base types / implemented interfaces\">: {Html.Encode(string.Join(", ", type.BaseTypes))}</span>"); }
                sb.Append($"<a href=\"files/{file.Slug}.html\" style=\"margin-left:auto;font-size:.82rem\" title=\"{Html.Encode(file.RelPath)}\">{Html.Encode(file.RelPath)}</a>");
                sb.Append("</div>");
                if (type.XmlSummary.Length > 0) { sb.Append($"<p class=\"lede\" style=\"margin:.4rem 0 0;font-size:.88rem\">{Html.Encode(type.XmlSummary)}</p>"); }
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
}
