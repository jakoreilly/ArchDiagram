using System.Text;
using System.Text.Json;
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
        sb.Append("<h1>Dependency Direction</h1>");

        var r = LayeringAnalyzer.Analyze(model);

        if (r.Layers.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">≡</div>"
                    + "<p>Not enough modules to analyse dependency direction. See the <a href=\"modules.html\">Modules</a> page.</p></div>");
            return sb.ToString();
        }

        if (r.Declared)
        {
            sb.Append("<p class=\"lede\">Modules assigned to your <strong>declared layers</strong> (top may depend on lower, "
                    + "never the reverse). Any dependency pointing <em>upward</em> — a lower layer reaching into a higher one — "
                    + "breaks the contract and is listed as a violation.</p>");
            sb.Append("<div class=\"tiles\">");
            Tile(sb, r.Layers.Count.ToString("N0"), "Layers");
            Tile(sb, r.Violations.Count.ToString("N0"), "Contract violations", r.Violations.Count > 0);
            if (r.Unassigned.Count > 0) { Tile(sb, r.Unassigned.Count.ToString("N0"), "Unassigned modules", true); }
            sb.Append("</div>");

            AppendViolations(sb, r, declared: true);
        }
        else
        {
            sb.Append("<p class=\"lede\">No layer contract is declared, so modules are placed into <strong>stability tiers</strong> "
                    + "by their Instability (how much they depend outward vs. are depended on) — orchestration at the top, "
                    + "foundational code at the bottom. Healthy dependencies point <em>downward</em>, toward more stable code. "
                    + "Edges that point the other way (a stable module depending on a less-stable one) break the "
                    + "<strong>Stable Dependencies Principle</strong> and are flagged as inversion candidates. "
                    + "Declare a contract (below) for a stricter, named-layer check.</p>");
            sb.Append("<div class=\"tiles\">");
            Tile(sb, r.Layers.Count.ToString("N0"), "Stability tiers");
            Tile(sb, r.Violations.Count.ToString("N0"), "Against-the-grain edges", r.Violations.Count > 0);
            sb.Append("</div>");
            AppendViolations(sb, r, declared: false);
        }

        AppendLayered3D(sb, model, r);

        // Tier / layer stack, top to bottom.
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

    /// <summary>A stacked-tier "3D chess board": each module pinned to an elevation by its tier,
    /// dependency edges between the planes, red where they point against the grain. Reuses the
    /// vendored ForceGraph3D bundle; degrades to the 2D tier list where WebGL is unavailable.</summary>
    private static void AppendLayered3D(StringBuilder sb, ProjectModel model, LayeringAnalyzer.Result r)
    {
        var g = ModuleGrouper.Build(model);
        var files = g.Modules.ToDictionary(m => m.Key, m => m.FileCount, StringComparer.Ordinal);
        var tierOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < r.Layers.Count; i++)
        {
            foreach (var key in r.Layers[i].Modules) { tierOf[key] = i; }
        }
        if (tierOf.Count < 2) { return; } // nothing meaningful to stack

        var against = new HashSet<(string, string)>(r.Violations.Select(v => (v.FromModule, v.ToModule)));
        var nodes = tierOf
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new { id = kv.Key, label = kv.Key, tier = kv.Value, files = files.GetValueOrDefault(kv.Key) });
        var links = g.Edges.Keys
            .Where(e => tierOf.ContainsKey(e.From) && tierOf.ContainsKey(e.To))
            .OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal)
            .Select(e => new { source = e.From, target = e.To, against = against.Contains((e.From, e.To)) });
        var payload = JsonSerializer.Serialize(new { tiers = r.Layers.Select(l => l.Name), nodes, links });

        sb.Append("<h2>Layered view <span class=\"badge\">3D</span></h2>");
        sb.Append("<p class=\"lede\">The tiers as a stack: each module floats at its stability level "
                + "(orchestration on top, foundational at the base). Dependency edges run between the planes — "
                + "<strong>red</strong> ones point <em>up</em>, against the grain. Drag to orbit, scroll to zoom, hover a node.</p>");
        sb.Append("<div class=\"select-row\"><button class=\"btn\" id=\"layered3d-reset\" type=\"button\">Reset view</button></div>");
        sb.Append("<div id=\"layered3d-root\" class=\"panel\" style=\"padding:0;position:relative\">"
                + "<div id=\"layered3d-canvas\" class=\"graph3d-canvas\"></div></div>");
        sb.Append("<script>window.ARCH_LAYERS=").Append(payload).Append(";</script>");
        sb.Append("<script src=\"assets/lib/3d-force-graph.min.js\"></script>");
        sb.Append("<script src=\"assets/layered3d.js\"></script>");
    }

    private static void AppendViolations(StringBuilder sb, LayeringAnalyzer.Result r, bool declared)
    {
        var title = declared ? "Contract violations" : "Against-the-grain dependencies";
        sb.Append($"<h2>{title} <span class=\"badge {(r.Violations.Count > 0 ? "warn" : "ok")}\">{r.Violations.Count}</span></h2>");
        if (r.Violations.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + (declared
                        ? "<p>Every cross-module dependency flows downward through the declared layers. The contract holds.</p>"
                        : "<p>Every cross-module dependency points toward more stable code. The Stable Dependencies Principle holds.</p>")
                    + "</div>");
            return;
        }
        sb.Append(declared
            ? "<p class=\"lede\">Each row is a dependency that points the wrong way — a module in a lower layer depends on a "
              + "module in a higher layer. Invert the dependency (e.g. via an interface owned by the lower layer) to restore the contract.</p>"
            : "<p class=\"lede\">Each row is a dependency from a <em>more stable</em> module to a <em>less stable</em> one — the "
              + "Stable Dependencies Principle in reverse. The stable module is now coupled to code more likely to change. Invert it "
              + "by having both depend on an abstraction owned by the stable side.</p>");
        sb.Append("<table class=\"grid\"><thead><tr><th>From module</th><th>From tier</th><th></th><th>To module</th><th>To tier</th></tr></thead><tbody>");
        var arrow = declared ? "↑ upward" : "↯ against grain";
        foreach (var v in r.Violations)
        {
            sb.Append($"<tr><td>{Html.Encode(v.FromModule)}</td><td><span class=\"badge\">{Html.Encode(v.FromLayer)}</span></td>"
                    + $"<td><span class=\"badge warn\">{arrow}</span></td>"
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
