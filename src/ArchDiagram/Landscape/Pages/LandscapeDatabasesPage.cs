using System.Text;
using ArchDiagram.Site;

namespace ArchDiagram.Landscape.Pages;

public static class LandscapeDatabasesPage
{
    public static string Body(LandscapeModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Shared Databases</h1>");
        sb.Append("""
<p class="lede">Databases discovered across the sites, matched by a normalized connection-string hash
(server + catalog). A database used by two or more sites is a real coupling point — a change to its
schema affects every site marked below.</p>
""");

        if (model.Databases.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🗄</div><p>None of the discovered sites recorded "
                + "a database connection. Database links surface when a project's connection string resolves to the same "
                + "server + catalog across sites.</p></div>");
            return sb.ToString();
        }

        sb.Append("<table class=\"grid\"><thead><tr><th>Database</th><th>Server</th><th>Catalog</th>");
        foreach (var s in model.Sites) { sb.Append($"<th>{Html.Encode(s.Id)}</th>"); }
        sb.Append("</tr></thead><tbody>");

        foreach (var db in model.Databases)
        {
            var shared = db.SiteIds.Count >= 2 ? " <span class=\"badge\">shared</span>" : "";
            sb.Append($"<tr><td>{Html.Encode(db.Label)}{shared}</td><td>{Html.Encode(db.Server)}</td><td>{Html.Encode(db.Catalog)}</td>");
            foreach (var s in model.Sites)
            {
                sb.Append(db.SiteIds.Contains(s.Id) ? "<td style=\"text-align:center\">✓</td>" : "<td></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
