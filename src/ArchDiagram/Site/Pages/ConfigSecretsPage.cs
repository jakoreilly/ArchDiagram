using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Config &amp; secrets review: every connection string the scan found (with the file
/// it lives in), which of them embed credentials committed to source, and the config files
/// present. A security-minded reviewer's first stop. Secret values are never stored or shown.</summary>
public static class ConfigSecretsPage
{
    private sealed record Found(string Project, DbUse Use);

    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Config &amp; Secrets</h1>");
        sb.Append("<p class=\"lede\">Connection strings and config discovered by the scan, and — the finding that "
                + "matters most — any connection string that embeds a <strong>credential committed to source</strong>. "
                + "Secret values are never stored or displayed; only their presence is flagged.</p>");

        var found = model.Projects
            .SelectMany(p => p.ConnectionStrings.Select(u => new Found(p.Name, u)))
            .OrderByDescending(f => f.Use.HasCredential)
            .ThenBy(f => f.Use.Evidence, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var configFiles = model.Files
            .Where(f => IsConfigFile(f.RelPath))
            .OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var withCred = found.Count(f => f.Use.HasCredential);

        sb.Append("<div class=\"tiles\">");
        Tile(sb, found.Count.ToString("N0"), "Connection strings");
        Tile(sb, model.Databases.Count.ToString("N0"), "Distinct databases");
        Tile(sb, withCred.ToString("N0"), "With embedded credentials", withCred > 0);
        Tile(sb, configFiles.Count.ToString("N0"), "Config files");
        sb.Append("</div>");

        // Embedded-credential finding first.
        sb.Append($"<h2>Credentials in source <span class=\"badge {(withCred > 0 ? "warn" : "ok")}\">{withCred}</span></h2>");
        if (withCred == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>No connection string was found with an embedded username/password. Where credentials are needed "
                    + "they appear to come from integrated auth or an external secret store — good.</p></div>");
        }
        else
        {
            sb.Append("<p class=\"lede\">These connection strings contain a username and/or password directly in a file "
                    + "under source control. Move them to a secret store (user-secrets, environment variables, Key Vault) "
                    + "and rotate any credential that has been committed.</p>");
            sb.Append("<table class=\"grid\"><thead><tr><th>Database</th><th>Server</th><th>Project</th><th>Evidence</th></tr></thead><tbody>");
            foreach (var f in found.Where(f => f.Use.HasCredential))
            {
                sb.Append($"<tr><td><span class=\"badge warn\">secret</span> {Html.Encode(f.Use.Label)}</td>"
                        + $"<td>{Html.Encode(f.Use.Server)}</td><td>{Html.Encode(f.Project)}</td>"
                        + $"<td><code>{Html.Encode(f.Use.Evidence)}</code></td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        // All connection strings.
        sb.Append("<h2>All connection strings</h2>");
        if (found.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🔑</div>"
                    + "<p>No connection strings were detected in code or config. (This scan reads <code>.cs</code>, "
                    + "<code>appsettings*.json</code> and <code>*.config</code>; strings built at runtime or held only in "
                    + "environment variables are not visible.)</p></div>");
        }
        else
        {
            sb.Append("<table class=\"grid\"><thead><tr><th>Database</th><th>Server</th><th>Catalog</th><th>Project</th><th>Credentials</th><th>Evidence</th></tr></thead><tbody>");
            foreach (var f in found)
            {
                var cred = f.Use.HasCredential ? "<span class=\"badge warn\">embedded</span>" : "<span class=\"badge ok\">none</span>";
                sb.Append($"<tr><td>{Html.Encode(f.Use.Label)}</td><td>{Html.Encode(f.Use.Server)}</td>"
                        + $"<td>{Html.Encode(f.Use.Catalog)}</td><td>{Html.Encode(f.Project)}</td>"
                        + $"<td>{cred}</td><td><code>{Html.Encode(f.Use.Evidence)}</code></td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        // Config files present.
        sb.Append($"<h2>Config files <span class=\"badge\">{configFiles.Count}</span></h2>");
        if (configFiles.Count == 0)
        {
            sb.Append("<p class=\"note\">No <code>appsettings*.json</code> or <code>*.config</code> files were found in the tree.</p>");
        }
        else
        {
            sb.Append("<p class=\"lede\">Configuration files in the tree — where environment settings and (ideally externalised) secrets live.</p>");
            sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Size</th></tr></thead><tbody>");
            foreach (var f in configFiles)
            {
                sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath)}</a></td>"
                        + $"<td>{StructurePage.FormatBytes(f.SizeBytes)}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<p class=\"note\">Detection is heuristic and syntax-only: it finds connection-string-shaped literals in "
                + "code and config, ignoring comments and Web.config transforms. It does not scan for API keys or tokens, "
                + "and cannot see secrets injected from environment variables or a secret manager at runtime.</p>");
        return sb.ToString();
    }

    private static bool IsConfigFile(string relPath)
    {
        var name = relPath.Split('/')[^1].ToLowerInvariant();
        return name.EndsWith(".config") || (name.StartsWith("appsettings") && name.EndsWith(".json"));
    }

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }
}
