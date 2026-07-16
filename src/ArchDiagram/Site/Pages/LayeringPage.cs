using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Layering &amp; dependency-direction view. With a declared layers contract it checks
/// every cross-module dependency and flags upward ones (a lower layer depending on a higher one);
/// without one it infers layers by longest dependency path so the reviewer still sees the shape.</summary>
public static class LayeringPage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Layering</h1>");

        var r = LayeringAnalyzer.Analyze(model);

        if (r.Layers.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">≡</div>"
                    + "<p>Not enough modules to derive layers. See the <a href=\"modules.html\">Modules</a> page.</p></div>");
            return sb.ToString();
        }

        if (r.Declared)
        {
            sb.Append("<p class=\"lede\">Modules assigned to the <strong>declared layers</strong> (top may depend on lower, "
                    + "never the reverse). Any dependency pointing <em>upward</em> — a lower layer reaching into a higher one — "
                    + "breaks the contract and is listed as a violation.</p>");
            sb.Append("<div class=\"tiles\">");
            Tile(sb, r.Layers.Count.ToString("N0"), "Layers");
            Tile(sb, r.Violations.Count.ToString("N0"), "Contract violations", r.Violations.Count > 0);
            if (r.Unassigned.Count > 0) { Tile(sb, r.Unassigned.Count.ToString("N0"), "Unassigned modules", true); }
            sb.Append("</div>");

            AppendViolations(sb, r);
        }
        else
        {
            sb.Append("<p class=\"lede\">No layers contract was declared, so these layers are <strong>inferred</strong> from the "
                    + "dependency graph: each module sits one level above everything it depends on. The bottom level is "
                    + "foundational (depended on by many, depends on little); the top orchestrates. Declare a contract to have "
                    + "violations checked — see below.</p>");
            sb.Append("<div class=\"tiles\">");
            Tile(sb, r.Layers.Count.ToString("N0"), "Inferred levels");
            sb.Append("</div>");
        }

        // Layer stack, top to bottom.
        foreach (var layer in r.Layers)
        {
            sb.Append($"<h3>{Html.Encode(layer.Name)} <span class=\"badge\">{layer.Modules.Count} module(s)</span></h3>");
            sb.Append("<div class=\"panel\">");
            sb.Append(layer.Modules.Count == 0
                ? "<span class=\"note\">(no modules)</span>"
                : string.Join(" ", layer.Modules.Select(m => $"<span class=\"badge accent\">{Html.Encode(m)}</span>")));
            sb.Append("</div>");
        }

        if (r.Declared && r.Unassigned.Count > 0)
        {
            sb.Append("<h2>Unassigned modules <span class=\"badge warn\">" + r.Unassigned.Count + "</span></h2>");
            sb.Append("<p class=\"lede\">These modules matched no layer in the contract. Add their namespace prefixes to a layer.</p>");
            sb.Append("<div class=\"panel\">" + string.Join(" ", r.Unassigned.Select(m => $"<span class=\"badge\">{Html.Encode(m)}</span>")) + "</div>");
        }

        AppendHowToDeclare(sb, r.Declared);
        return sb.ToString();
    }

    private static void AppendViolations(StringBuilder sb, LayeringAnalyzer.Result r)
    {
        sb.Append($"<h2>Contract violations <span class=\"badge {(r.Violations.Count > 0 ? "warn" : "ok")}\">{r.Violations.Count}</span></h2>");
        if (r.Violations.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>Every cross-module dependency flows downward through the declared layers. The contract holds.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Each row is a dependency that points the wrong way — a module in a lower layer depends on a "
                + "module in a higher layer. Invert the dependency (e.g. via an interface owned by the lower layer) to restore the contract.</p>");
        sb.Append("<table class=\"grid\"><thead><tr><th>From module</th><th>From layer</th><th></th><th>To module</th><th>To layer</th></tr></thead><tbody>");
        foreach (var v in r.Violations)
        {
            sb.Append($"<tr><td>{Html.Encode(v.FromModule)}</td><td><span class=\"badge\">{Html.Encode(v.FromLayer)}</span></td>"
                    + $"<td><span class=\"badge warn\">↑ upward</span></td>"
                    + $"<td>{Html.Encode(v.ToModule)}</td><td><span class=\"badge\">{Html.Encode(v.ToLayer)}</span></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendHowToDeclare(StringBuilder sb, bool declared)
    {
        sb.Append("<details class=\"legend\"" + (declared ? "" : " open") + "><summary>Declare a layering contract</summary>");
        sb.Append("<p class=\"note\">Drop an <code>archdiagram.layers.json</code> file at the source root, listing layers "
                + "top-to-bottom. Each layer names the namespace/module prefixes that belong to it. The scan then checks that "
                + "dependencies only ever flow downward.</p>");
        sb.Append("<pre style=\"white-space:pre;padding:.75rem;background:var(--bg-sunken);border-radius:8px;overflow:auto\">"
                + Html.Encode("[\n  { \"name\": \"Presentation\", \"namespaces\": [\"MyApp.Web\", \"MyApp.Api\"] },\n"
                + "  { \"name\": \"Application\",  \"namespaces\": [\"MyApp.Services\"] },\n"
                + "  { \"name\": \"Domain\",       \"namespaces\": [\"MyApp.Domain\", \"MyApp.Core\"] }\n]")
                + "</pre></details>");
    }

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }
}
