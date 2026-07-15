using System.Text;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;
using ArchDiagram.Site;

namespace ArchDiagram.Landscape.Pages;

public static class LandscapeIndexPage
{
    public static string Body(LandscapeModel model, int maxNodes, string generatedOn)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Landscape — Cross-Site Overview</h1>");
        sb.Append($"""
<p class="lede">This landscape cross-references the sites generated beneath this folder. Each box is one
site — click it to open that site's own report. Solid lines are package dependencies between sites;
dashed lines are heuristic cross-service calls; database cylinders are shared by two or more sites.
Generated on {Html.Encode(generatedOn)}.</p>
""");

        if (model.Sites.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⇄</div><p>No generated sites were found "
                + "beside this folder. Generate at least two sites first (run ArchDiagram on each repo), then re-run the landscape.</p></div>");
            return sb.ToString();
        }

        var sharedDbs = model.Databases.Where(d => d.SiteIds.Count >= 2).ToList();

        // Stat tiles.
        sb.Append("<div class=\"tiles\">");
        Tile(sb, model.Sites.Count.ToString("N0"), "Sites");
        Tile(sb, sharedDbs.Count.ToString("N0"), "Shared databases");
        Tile(sb, model.PackageEdges.Count.ToString("N0"), "Package links");
        Tile(sb, model.ServiceCalls.Count.ToString("N0"), "Cross-service links");
        sb.Append("</div>");

        sb.Append("<h2>Interconnections</h2>");
        sb.Append("<p class=\"lede\">Sites, the databases they share, packages one site produces for another, and heuristic calls that cross site boundaries. Hover a node for details; click a site to open it. Use the filters to focus on the layer you care about.</p>");
        sb.Append("""
<div class="landscape-filters" id="landscape-filters" hidden>
  <label class="lf-check"><input type="checkbox" id="lf-calls" checked> Cross-service calls</label>
  <label class="lf-range">Min calls <input type="range" id="lf-threshold" min="0" max="1300" step="25" value="150"> <span class="filter-count" id="lf-threshold-val">150</span></label>
  <label class="lf-check"><input type="checkbox" id="lf-packages"> Shared packages</label>
  <label class="lf-check"><input type="checkbox" id="lf-pkglinks" checked> Package links</label>
  <span class="filter-count" id="lf-summary" style="margin-left:auto"></span>
</div>
""");
        sb.Append(PageTemplate.DiagramBlock("landscape", BuildDiagram(model, sharedDbs, maxNodes), "landscape-overview", deferred: true));
        sb.Append(LandscapeLegend());

        sb.Append("<p class=\"note\">Links appear only where sites genuinely overlap — a shared database connection string, "
            + "one site's project referenced as another's package, or a call to a type defined in another site. "
            + "If sites share nothing, they correctly appear as separate islands.</p>");

        if (model.Diagnostics.Count > 0)
        {
            sb.Append($"<h2>Scan diagnostics <span class=\"badge warn\">{model.Diagnostics.Count}</span></h2>");
            sb.Append("<div class=\"panel diag-list\"><ul>");
            foreach (var d in model.Diagnostics.Take(50)) { sb.Append($"<li>{Html.Encode(d)}</li>"); }
            sb.Append("</ul></div>");
        }

        return sb.ToString();
    }

    private static void Tile(StringBuilder sb, string num, string label)
    {
        var zero = num is "0" or "0.0" ? " tile-zero" : "";
        sb.Append($"<div class=\"tile{zero}\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }

    // Landscape-specific legend: only the shapes this diagram actually uses
    // (sites, shared external packages, solid package links, dashed calls).
    private static string LandscapeLegend() => """
<details class="legend"><summary>What the shapes and colours mean</summary>
<div class="legend-grid">
  <span class="legend-item"><span class="legend-swatch" style="background:#dcecf9;border-color:#2f6fab"></span>Site (click to open its report)</span>
  <span class="legend-item"><span class="legend-swatch hex" style="background:#f0f0f0;border-color:#8a8a8a"></span>Shared external package</span>
  <span class="legend-item"><span class="legend-line"></span>Package one site produces for another (solid)</span>
  <span class="legend-item"><span class="legend-line dashed"></span>Cross-service call / shared package (dashed)</span>
</div>
<p class="note" style="margin:.6rem 0 0">Dashed call edges are drawn thicker the more calls they carry. Grey hexagons are packages that live outside this codebase.</p>
</details>
""";

    public static Diagram BuildDiagram(LandscapeModel model, List<SharedDb> sharedDbs, int maxNodes)
    {
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();

        foreach (var s in model.Sites)
        {
            var m = s.Model;
            var tooltip = $"Site: {s.Id}\nRoot: {m.RootName}\nFiles: {m.Files.Count:N0} · Projects: {m.Projects.Count:N0} · Databases: {m.Databases.Count:N0}";
            nodes.Add(new DiagramNode("site:" + s.Id, m.RootName, "service", Tooltip: tooltip, Href: s.IndexHref));
        }

        foreach (var db in sharedDbs)
        {
            var tooltip = $"Shared database (matched by connection-string hash)\nServer: {(db.Server.Length > 0 ? db.Server : "unknown")}\nCatalog: {(db.Catalog.Length > 0 ? db.Catalog : "unknown")}\nUsed by {db.SiteIds.Count} sites";
            nodes.Add(new DiagramNode("db:" + db.Hash, db.Label, "database", NodeShape.Database, tooltip));
            foreach (var siteId in db.SiteIds)
            {
                edges.Add(new DiagramEdge("site:" + siteId, "db:" + db.Hash, "sql"));
            }
        }

        // Package producer/consumer edges, merged into one labelled edge per site pair.
        foreach (var g in model.PackageEdges.GroupBy(e => (e.FromSiteId, e.ToSiteId)))
        {
            var label = g.Count() == 1 ? g.First().Package : $"{g.Count()} packages";
            edges.Add(new DiagramEdge("site:" + g.Key.FromSiteId, "site:" + g.Key.ToSiteId, label));
        }

        // Shared external packages: hexagon nodes, dashed edges.
        foreach (var sp in model.SharedPackages)
        {
            var id = "ext:" + sp.Name;
            nodes.Add(new DiagramNode(id, sp.Name, "external", NodeShape.Hexagon, $"External package shared by {sp.SiteIds.Count} sites: {sp.Name}"));
            foreach (var siteId in sp.SiteIds)
            {
                edges.Add(new DiagramEdge("site:" + siteId, id, "", Dashed: true));
            }
        }

        // Cross-service calls: dashed (heuristic), labelled with the count.
        foreach (var c in model.ServiceCalls)
        {
            edges.Add(new DiagramEdge("site:" + c.FromSiteId, "site:" + c.ToSiteId, $"{c.Count} calls", Dashed: true));
        }

        var (n, e) = GraphReducer.TrimToMax(nodes, edges.DistinctBy(x => (x.FromId, x.ToId, x.Label, x.Dashed)).ToList(), maxNodes);
        return MermaidRenderer.Render(n, e, totalNodes: nodes.Count);
    }
}
