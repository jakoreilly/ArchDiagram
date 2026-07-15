using System.Globalization;
using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Architecture Metrics: Robert C. Martin's module-level Instability / Abstractness /
/// Distance-from-the-main-sequence, propagation cost, and dependency cycles. All server-rendered
/// and heuristic (module level, syntax-only analysis).</summary>
public static class MetricsPage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Architecture Metrics</h1>");

        var r = ArchitectureMetrics.Compute(model);
        if (r.Modules.Count < 2)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">📐</div>"
                    + "<p>Not enough modules to compute architecture metrics. See the "
                    + "<a href=\"modules.html\">Modules</a> page.</p></div>");
            return sb.ToString();
        }

        var by = r.Mode == "namespace" ? "C# namespace" : "top-level folder";
        sb.Append($"<p class=\"lede\">Quantitative health signals per module (grouped by {by}). "
                + "<strong>Instability</strong> I = Ce/(Ca+Ce) — how much a module depends outward vs. is depended on. "
                + "<strong>Abstractness</strong> A = abstract types ÷ total types. "
                + "<strong>Distance</strong> D = |A + I − 1| — how far a module sits from the ideal "
                + "“main sequence” balance. Lower D is healthier.</p>");
        sb.Append("<p class=\"note\">Heuristic and module-level: analysis is syntax-only (no compilation), "
                + "abstractness counts <code>interface</code> and <code>abstract</code> types, and namespace "
                + "import edges are capped — treat these as strong hints, not exact measures.</p>");

        var worst = r.Modules[0]; // ordered Distance desc
        sb.Append("<div class=\"tiles\">");
        Tile(sb, r.Modules.Count.ToString("N0"), "Modules");
        Tile(sb, r.PropagationCost.ToString("P0", CultureInfo.InvariantCulture), "Propagation cost");
        Tile(sb, r.Cycles.Count.ToString("N0"), "Dependency cycles", r.Cycles.Count > 0);
        Tile(sb, worst.Distance.ToString("F2", CultureInfo.InvariantCulture), "Worst distance (D)", worst.Distance > 0.6);
        sb.Append("</div>");

        // Main-sequence scatter.
        var prefix = CommonPrefix(r.Modules.Select(m => m.Key).Where(k => !k.StartsWith('(')).ToList());
        sb.Append("<h2>Main sequence <span class=\"badge\">Instability vs. Abstractness</span></h2>");
        sb.Append("<p class=\"lede\">Each dot is a module at (Instability, Abstractness). The diagonal is the "
                + "<strong>main sequence</strong> (A + I = 1) — the healthy balance. Bottom-left is the "
                + "<strong>zone of pain</strong> (concrete &amp; heavily depended-on — rigid); top-right is the "
                + "<strong>zone of uselessness</strong> (abstract &amp; unused). Dot size = files; colour = distance.</p>");
        sb.Append($"<div class=\"metrics-scatter\">{BuildScatter(r.Modules, prefix)}</div>");

        // Metrics table (worst distance first).
        if (prefix.Length > 0)
        {
            sb.Append($"<p class=\"note\">Module names share the prefix <code>{Html.Encode(prefix)}</code>, omitted below.</p>");
        }
        sb.Append("<table class=\"grid\"><thead><tr><th>Module</th><th>Files</th><th>Ca</th><th>Ce</th>"
                + "<th>Instability</th><th>Abstractness</th><th>Distance</th></tr></thead><tbody>");
        foreach (var m in r.Modules)
        {
            sb.Append($"<tr><td>{Html.Encode(Strip(m.Key, prefix))}</td><td>{m.Files:N0}</td>"
                    + $"<td>{m.Ca:N0}</td><td>{m.Ce:N0}</td>"
                    + $"<td>{m.Instability.ToString("F2", CultureInfo.InvariantCulture)}</td>"
                    + $"<td>{m.Abstractness.ToString("F2", CultureInfo.InvariantCulture)}</td>"
                    + $"<td><span class=\"badge {DistanceClass(m.Distance)}\">{m.Distance.ToString("F2", CultureInfo.InvariantCulture)}</span></td></tr>");
        }
        sb.Append("</tbody></table>");

        AppendCycles(sb, r, prefix);
        AppendLegend(sb);
        return sb.ToString();
    }

    private static void AppendCycles(StringBuilder sb, ArchitectureMetrics.Result r, string prefix)
    {
        sb.Append($"<h2>Dependency cycles <span class=\"badge {(r.Cycles.Count > 0 ? "warn" : "ok")}\">{r.Cycles.Count}</span></h2>");
        if (r.ClosureSkipped)
        {
            sb.Append("<p class=\"note\">Too many modules to compute cycles and propagation cost efficiently — skipped.</p>");
            return;
        }
        if (r.Cycles.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>No dependency cycles between modules. Dependencies flow one way — good.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Groups of modules that (transitively) depend on each other. Cycles make "
                + "modules impossible to build, test or understand in isolation — prime refactor targets.</p>");
        sb.Append("<div class=\"panel\"><ul class=\"member-list\" style=\"font-family:inherit\">");
        var first = true;
        foreach (var group in r.Cycles)
        {
            var style = first ? " style=\"border-top:none\"" : "";
            first = false;
            var chain = string.Join(" → ", group.Select(k => Html.Encode(Strip(k, prefix))));
            sb.Append($"<li{style}><span class=\"badge warn\">cycle</span> {chain} → …</li>");
        }
        sb.Append("</ul></div>");
    }

    private static void AppendLegend(StringBuilder sb)
    {
        sb.Append("""
<details class="legend"><summary>What the numbers mean</summary>
<div class="legend-grid" style="flex-direction:column;gap:.35rem">
  <span class="legend-item"><strong>Ca</strong> (afferent) — distinct modules that depend on this one. High = risky to change.</span>
  <span class="legend-item"><strong>Ce</strong> (efferent) — distinct modules this one depends on. High = knows about a lot.</span>
  <span class="legend-item"><strong>Instability I = Ce/(Ca+Ce)</strong> — 0 = stable (depended-on, depends on little); 1 = unstable.</span>
  <span class="legend-item"><strong>Abstractness A</strong> — share of interface/abstract types. Stable modules should be abstract.</span>
  <span class="legend-item"><strong>Distance D = |A + I − 1|</strong> — distance from the main sequence; lower is healthier.</span>
  <span class="legend-item"><strong>Propagation cost</strong> — density of the transitive dependency matrix: how far a change can reach.</span>
</div>
</details>
""");
    }

    // ---- Main-sequence SVG (pre-split, offline, theme-aware) ----

    private const double W = 480, H = 480, Pad = 44;

    private static string BuildScatter(IReadOnlyList<ArchitectureMetrics.ModuleMetric> modules, string prefix)
    {
        var maxFiles = Math.Max(1, modules.Max(m => m.Files));
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {W:0} {H:0}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"Main-sequence scatter of module Instability versus Abstractness\">");
        Axes(sb);
        // Main sequence line: (I=0,A=1) → (I=1,A=0).
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{MapX(0):0.#}\" y1=\"{MapY(1):0.#}\" x2=\"{MapX(1):0.#}\" y2=\"{MapY(0):0.#}\" stroke=\"var(--accent)\" stroke-width=\"1.5\" stroke-dasharray=\"5 4\"/>");
        // Zone labels.
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{MapX(0.02):0.#}\" y=\"{MapY(0.05):0.#}\" font-size=\"11\" fill=\"var(--text-soft)\">zone of pain</text>");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{MapX(0.62):0.#}\" y=\"{MapY(0.96):0.#}\" font-size=\"11\" fill=\"var(--text-soft)\">zone of uselessness</text>");
        foreach (var m in modules.OrderBy(m => m.Key, StringComparer.Ordinal)) { Plot(sb, m, maxFiles, prefix); }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static void Axes(StringBuilder sb)
    {
        // Plot border + axis titles.
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{MapX(0):0.#}\" y=\"{MapY(1):0.#}\" width=\"{W - 2 * Pad:0.#}\" height=\"{H - 2 * Pad:0.#}\" fill=\"none\" stroke=\"var(--border)\" stroke-width=\"1\"/>");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{W / 2:0.#}\" y=\"{H - 8:0.#}\" font-size=\"12\" text-anchor=\"middle\" fill=\"var(--text-soft)\">Instability →</text>");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"14\" y=\"{H / 2:0.#}\" font-size=\"12\" text-anchor=\"middle\" fill=\"var(--text-soft)\" transform=\"rotate(-90 14 {H / 2:0.#})\">Abstractness →</text>");
    }

    private static void Plot(StringBuilder sb, ArchitectureMetrics.ModuleMetric m, int maxFiles, string prefix)
    {
        var cx = MapX(m.Instability);
        var cy = MapY(m.Abstractness);
        var radius = 4 + (6.0 * m.Files / maxFiles);
        var fill = m.Distance <= 0.3 ? "var(--ok)" : m.Distance <= 0.6 ? "var(--accent)" : "var(--danger)";
        var title = $"{Strip(m.Key, prefix)} · I={m.Instability.ToString("F2", CultureInfo.InvariantCulture)}"
                  + $" A={m.Abstractness.ToString("F2", CultureInfo.InvariantCulture)}"
                  + $" D={m.Distance.ToString("F2", CultureInfo.InvariantCulture)}";
        sb.Append(CultureInfo.InvariantCulture,
            $"<circle cx=\"{cx:0.#}\" cy=\"{cy:0.#}\" r=\"{radius:0.#}\" fill=\"{fill}\" fill-opacity=\"0.75\" stroke=\"var(--bg-panel)\" stroke-width=\"1\"><title>{Html.Encode(title)}</title></circle>");
    }

    private static double MapX(double instability) => Pad + instability * (W - 2 * Pad);
    private static double MapY(double abstractness) => (H - Pad) - abstractness * (H - 2 * Pad);

    private static string DistanceClass(double d) => d <= 0.3 ? "ok" : d <= 0.6 ? "" : "warn";

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }

    private static string Strip(string key, string prefix) =>
        prefix.Length > 0 && key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length
            ? key[prefix.Length..] : key;

    /// <summary>Longest shared dotted/slashed prefix (trimmed to a boundary). Empty if none.</summary>
    private static string CommonPrefix(IReadOnlyList<string> keys)
    {
        if (keys.Count < 2) { return ""; }
        var prefix = keys[0];
        foreach (var k in keys.Skip(1))
        {
            var n = Math.Min(prefix.Length, k.Length);
            var i = 0;
            while (i < n && prefix[i] == k[i]) { i++; }
            prefix = prefix[..i];
            if (prefix.Length == 0) { return ""; }
        }
        var boundary = prefix.LastIndexOfAny(['.', '/']);
        return boundary <= 0 ? "" : prefix[..(boundary + 1)];
    }
}
