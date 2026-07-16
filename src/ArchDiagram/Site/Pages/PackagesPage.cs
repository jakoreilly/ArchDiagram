using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>External-dependency (SBOM-lite) view: every NuGet package the projects reference,
/// which projects use it, its declared version(s), and — the reviewer's headline — packages
/// pulled at more than one version across the solution (version drift). Server-rendered, pure.</summary>
public static class PackagesPage
{
    private sealed record Usage(string Package, string Version, string Project);

    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Dependencies &amp; Stack</h1>");

        AppendStack(sb, model);

        sb.Append("<h2>Packages</h2>");
        sb.Append("<p class=\"lede\">Every external NuGet package referenced by the projects in this codebase, "
                + "which projects use it, and the version(s) declared. <strong>Version drift</strong> — the same "
                + "package pulled at different versions across projects — is called out first: it is a common source "
                + "of subtle runtime bugs and duplicate assemblies.</p>");

        var usages = model.Projects
            .SelectMany(p => p.Packages.Select(pk => new Usage(pk.Name, pk.Version, p.Name)))
            .ToList();

        if (usages.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">📦</div>"
                    + "<p>No external <code>PackageReference</code> entries were found in the discovered projects. "
                    + "Either the projects have no NuGet dependencies, or versions are managed somewhere this "
                    + "syntax-only scan does not read (e.g. a lock file).</p></div>");
            return sb.ToString();
        }

        var byPackage = usages
            .GroupBy(u => u.Package, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A package has drift when its usages declare 2+ distinct non-empty versions.
        var drifted = byPackage
            .Where(g => g.Select(u => u.Version).Where(v => v.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .ToList();
        var shared = byPackage.Where(g => g.Select(u => u.Project).Distinct(StringComparer.Ordinal).Count() >= 2).ToList();

        sb.Append("<div class=\"tiles\">");
        Tile(sb, byPackage.Count.ToString("N0"), "Distinct packages");
        Tile(sb, shared.Count.ToString("N0"), "Shared by ≥ 2 projects");
        Tile(sb, drifted.Count.ToString("N0"), "Version-drift packages", drifted.Count > 0);
        sb.Append("</div>");

        // Version drift first — the actionable finding.
        sb.Append($"<h2>Version drift <span class=\"badge {(drifted.Count > 0 ? "warn" : "ok")}\">{drifted.Count}</span></h2>");
        if (drifted.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>Every package that is referenced more than once uses a single version. No drift detected.</p></div>");
        }
        else
        {
            sb.Append("<p class=\"lede\">These packages are referenced at more than one version. Align them on a single "
                    + "version (or adopt Central Package Management) to avoid duplicate assemblies and version-conflict bugs.</p>");
            sb.Append("<table class=\"grid\"><thead><tr><th>Package</th><th>Versions</th><th>Projects</th></tr></thead><tbody>");
            foreach (var g in drifted)
            {
                var versions = g.Select(u => u.Version.Length > 0 ? u.Version : "(unspecified)")
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.Ordinal);
                var vlist = string.Join(" ", versions.Select(v => $"<span class=\"badge warn\">{Html.Encode(v)}</span>"));
                var projs = string.Join(", ", g.Select(u => u.Project).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).Select(Html.Encode));
                sb.Append($"<tr><td><code>{Html.Encode(g.Key)}</code></td><td>{vlist}</td><td>{projs}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        // Full inventory.
        sb.Append("<h2>Full package inventory</h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>Package</th><th>Version(s)</th><th>Used by</th><th>Projects</th></tr></thead><tbody>");
        foreach (var g in byPackage)
        {
            var versions = g.Select(u => u.Version.Length > 0 ? u.Version : "(unspecified)")
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.Ordinal).ToList();
            var drift = versions.Count(v => v != "(unspecified)") >= 2;
            var vcell = string.Join(" ", versions.Select(v => $"<span class=\"badge {(drift ? "warn" : "")}\">{Html.Encode(v)}</span>"));
            var projList = g.Select(u => u.Project).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
            sb.Append($"<tr><td><code>{Html.Encode(g.Key)}</code></td><td>{vcell}</td>"
                    + $"<td>{projList.Count:N0} project(s)</td><td>{string.Join(", ", projList.Select(Html.Encode))}</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<p class=\"note\">Versions are read from <code>PackageReference</code> in each <code>.csproj</code>. "
                + "\"(unspecified)\" means no inline version — common under Central Package Management, where the version "
                + "lives in <code>Directory.Packages.props</code> (not read by this syntax-only scan).</p>");
        return sb.ToString();
    }

    /// <summary>Detected frameworks grouped by category, and the outward integration points
    /// (databases + integration-category tech) — the stack at a glance.</summary>
    private static void AppendStack(StringBuilder sb, ProjectModel model)
    {
        var stack = Analysis.TechStack.Detect(model);
        sb.Append("<h2>Detected stack</h2>");
        if (stack.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🧩</div>"
                    + "<p>No recognised frameworks were detected from package references or imports.</p></div>");
        }
        else
        {
            sb.Append("<p class=\"lede\">Frameworks and libraries recognised from package references and imports, grouped by role. "
                    + "Unknown packages are listed in full below, not guessed at here.</p>");
            sb.Append("<div class=\"panel\">");
            foreach (var g in stack.GroupBy(t => t.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.Append($"<div style=\"margin:.35rem 0\"><span class=\"lbl\" style=\"display:inline-block;min-width:11rem\">{Html.Encode(g.Key)}</span> "
                        + string.Join(" ", g.OrderBy(t => t.Name, StringComparer.Ordinal)
                            .Select(t => $"<span class=\"badge accent\" title=\"{Html.Encode(string.Join(", ", t.UsedBy))}\">{Html.Encode(t.Name)}</span>"))
                        + "</div>");
            }
            sb.Append("</div>");
        }

        // Integration points = databases + integration-category tech (the system's outward edges).
        var integ = stack.Where(t => Analysis.TechStack.IntegrationCategories.Contains(t.Category)).ToList();
        sb.Append("<h2>Integration points <span class=\"badge\">" + (model.Databases.Count + integ.Count) + "</span></h2>");
        if (model.Databases.Count == 0 && integ.Count == 0)
        {
            sb.Append("<p class=\"note\">No databases or integration frameworks (messaging, cloud, HTTP, cache) were detected. "
                    + "This looks self-contained, or its integrations aren't visible to a syntax-only scan.</p>");
        }
        else
        {
            sb.Append("<div class=\"panel\">");
            foreach (var db in model.Databases)
            {
                sb.Append($"<div style=\"margin:.3rem 0\"><span class=\"badge\">database</span> <strong>{Html.Encode(db.Label)}</strong> "
                        + $"<span class=\"filter-count\">{Html.Encode(db.Server)}{(db.Catalog.Length > 0 ? " / " + Html.Encode(db.Catalog) : "")}</span> "
                        + "<a href=\"config.html\" style=\"font-size:.8rem\">Config &amp; Secrets →</a></div>");
            }
            foreach (var t in integ)
            {
                sb.Append($"<div style=\"margin:.3rem 0\"><span class=\"badge\">{Html.Encode(t.Category.ToLowerInvariant())}</span> {Html.Encode(t.Name)} "
                        + $"<span class=\"filter-count\">{Html.Encode(string.Join(", ", t.UsedBy))}</span></div>");
            }
            sb.Append("</div>");
        }
    }

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }
}
