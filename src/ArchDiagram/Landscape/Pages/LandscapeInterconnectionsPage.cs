using System.Text;
using ArchDiagram.Site;

namespace ArchDiagram.Landscape.Pages;

public static class LandscapeInterconnectionsPage
{
    public static string Body(LandscapeModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Interconnections</h1>");
        sb.Append("""
<p class="lede">The concrete links behind the overview graph: which site consumes a package produced by
another, and which methods call across site boundaries. Cross-service calls are heuristic (matched by
type name), so treat them as leads rather than proof.</p>
""");

        sb.Append("<h2>Package dependencies</h2>");
        if (model.PackageEdges.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><p>No package produced by one site is referenced by another. "
                + "This link surfaces when a site's project name matches another site's package reference.</p></div>");
        }
        else
        {
            sb.Append("<table class=\"grid\"><thead><tr><th>Consumer site</th><th>Package</th><th>Producer site</th></tr></thead><tbody>");
            foreach (var e in model.PackageEdges)
            {
                sb.Append($"<tr><td>{Html.Encode(e.FromSiteId)}</td><td>{Html.Encode(e.Package)}</td><td>{Html.Encode(e.ToSiteId)}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<h2>Cross-service calls</h2>");
        if (model.ServiceCalls.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><p>No calls were detected to a type defined in another site. "
                + "The sites may be genuinely independent, or use names that don't match across repos.</p></div>");
        }
        else
        {
            sb.Append("<table class=\"grid\"><thead><tr><th>From site</th><th>To site</th><th>Calls</th><th>Example</th></tr></thead><tbody>");
            foreach (var c in model.ServiceCalls)
            {
                sb.Append($"<tr><td>{Html.Encode(c.FromSiteId)}</td><td>{Html.Encode(c.ToSiteId)}</td><td>{c.Count:N0}</td><td>{Html.Encode(c.Sample)}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        return sb.ToString();
    }
}
