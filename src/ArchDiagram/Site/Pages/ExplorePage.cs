using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Explore page: a client-side query console over the dependency model embedded in the
/// page. The full node/edge payload (the same one the 3D graph uses) is inlined as
/// <c>window.ARCH_QUERY</c>; a fixed, discoverable vocabulary of predicates
/// (imports/importedby/reaches/orphans/folder/lang/path + numeric filters) runs entirely in the
/// browser — no server, no network, works from file://. See site.js for the engine.</summary>
public static class ExplorePage
{
    public static string Body(ProjectModel model, string graphJson)
    {
        if (!GraphPage.HasData(model))
        {
            return "<h1>Explore</h1>"
                + "<div class=\"panel empty-state\"><div class=\"big\">🔎</div><p>No file-to-file "
                + "dependencies were detected, so there is nothing to query yet. See the "
                + "<a href=\"dependencies.html\">Dependencies</a> page.</p></div>";
        }

        var sb = new StringBuilder();
        sb.Append("<h1>Explore</h1>");
        sb.Append("<p class=\"lede\">Ask the dependency graph questions. Type a query and press Enter — "
                + "everything runs in your browser against the model embedded in this page. Click any result "
                + "to open its file. Examples below; click one to try it.</p>");

        sb.Append("<div id=\"query-console\">");
        sb.Append("<div class=\"select-row\">"
                + "<input class=\"filter-input\" id=\"query-input\" type=\"search\" autocomplete=\"off\" spellcheck=\"false\" "
                + "placeholder=\"e.g.  importedby: Pipeline   ·   reaches: database   ·   cog &gt; 20   ·   orphans\">"
                + "<span class=\"filter-count\" id=\"query-count\"></span></div>");

        // Example chips — clicking one fills the box and runs it (wired in site.js). The examples
        // are static verbs; the values are illustrative and safe on any codebase (they simply
        // return nothing if a name doesn't match).
        sb.Append("<div class=\"lang-legend\" id=\"query-examples\" style=\"gap:.4rem;margin:.2rem 0 .6rem\">");
        foreach (var ex in new[] { "importedby: .cs", "imports: Pipeline", "reaches: database", "path: Program Model", "orphans", "cog > 20", "fanin > 5", "folder: Analysis" })
        {
            sb.Append($"<button type=\"button\" class=\"btn query-example\" style=\"padding:.15rem .5rem;font-size:.75rem\">{Html.Encode(ex)}</button>");
        }
        sb.Append("</div>");

        sb.Append("<details class=\"legend\"><summary>Query reference</summary><div class=\"legend-grid\" style=\"flex-direction:column;gap:.3rem\">");
        foreach (var (q, meaning) in QueryReference)
        {
            sb.Append($"<span class=\"legend-item\"><code>{Html.Encode(q)}</code> — {Html.Encode(meaning)}</span>");
        }
        sb.Append("</div><p class=\"note\" style=\"margin:.5rem 0 0\">Name matches are case-insensitive substrings of the file path. "
                + "Numeric fields: <code>loc</code>, <code>cog</code> (peak cognitive complexity), <code>fanin</code>, <code>fanout</code> "
                + "— use <code>&gt;</code>, <code>&gt;=</code>, <code>&lt;</code>, <code>&lt;=</code>, or <code>=</code>.</p></details>");

        sb.Append("<ul class=\"member-list\" id=\"query-results\"></ul>");
        sb.Append("</div>");

        // Same payload the 3D graph consumes; inlined so the console works offline from file://.
        sb.Append("<script>window.ARCH_QUERY=").Append(graphJson).Append(";</script>");
        return sb.ToString();
    }

    private static readonly (string, string)[] QueryReference =
    [
        ("imports: X", "files that X depends on (X's outgoing edges)"),
        ("importedby: X", "files that depend on X (X's incoming edges) — the blast radius of changing X"),
        ("reaches: X", "everything X can reach transitively (downstream)"),
        ("reachedby: X", "everything that can reach X transitively (upstream)"),
        ("path: A B", "a shortest dependency path from A to B, if one exists"),
        ("orphans", "files with no dependency links at all (dead-code candidates)"),
        ("folder: X", "files whose top-level folder is X"),
        ("lang: X", "files in language X (e.g. C#, Python)"),
        ("cog > N", "files whose peak method complexity exceeds N (also loc / fanin / fanout)"),
    ];
}
