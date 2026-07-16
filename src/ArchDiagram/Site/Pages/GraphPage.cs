using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;


/// <summary>The interactive 3D dependency graph page. All rendering happens
/// client-side in graph3d.js against a vendored WebGL bundle and the local
/// graph.json; this page only lays out the controls, canvas and side panel.
/// <see cref="Embed"/> renders the reusable widget (also used, compact, on the
/// Overview page); <see cref="Body"/> wraps it with the full-page heading/lede.</summary>
public static class GraphPage
{
    /// <summary>True when the graph has at least one file-to-file dependency to plot.</summary>
    public static bool HasData(ProjectModel model) =>
        model.FileDependencies.Count(e => e.ToSlug.Length > 0) > 0;

    public static string Body(ProjectModel model)
    {
        if (!HasData(model))
        {
            return "<h1>3D Dependency Graph</h1>" +
                "<div class=\"panel empty-state\"><div class=\"big\">\U0001f578</div><p>No file-to-file " +
                "dependencies were detected, so there is nothing to plot. See the " +
                "<a href=\"dependencies.html\">Dependencies</a> page for details.</p></div>";
        }

        var sb = new StringBuilder();
        sb.Append("<h1>3D Dependency Graph</h1>");
        sb.Append("""
<p class="lede">Explore how files depend on each other in 3D. Drag to orbit, scroll to zoom, and
<strong>click any node to focus it</strong> — the graph re-centres on that file and its neighbours
unfold around it while the rest fades back. This view encodes five <em>data</em> channels (see the
legend); it is not five geometric dimensions — a screen is three at most.
Toggle import and call edges independently, or type in the filter box to spotlight files by path, folder or language.
Click a node and choose <strong>↯ Trace data flow</strong> to light up everything reachable downstream from it —
the blast radius of a change or the paths a request can take — coloured by hops from that entry, with an optional animated pulse.</p>
""");
        sb.Append(Embed(model, compact: false, relRoot: ""));
        return sb.ToString();
    }

    /// <summary>The reusable graph widget: controls, canvas + side panel, legend, the
    /// inline payload and the bundle/controller scripts. In <paramref name="compact"/>
    /// mode the canvas is shorter and the hops/colour/highlight controls are dropped
    /// (search + hide-tests + reset remain), with a link to the full page.</summary>
    /// <param name="relRoot">"" for root pages, "../" for pages under files/.</param>
    public static string Embed(ProjectModel model, bool compact, string relRoot)
    {
        if (!HasData(model)) { return ""; }

        var sb = new StringBuilder();
        var canvasClass = compact ? "graph3d-canvas graph3d-compact" : "graph3d-canvas";

        // Controls row (reuses .select-row / .btn / .lf-* patterns). graph3d.js guards
        // every control lookup, so the compact subset is safe to render on its own.
        sb.Append("<div class=\"select-row\" id=\"graph3d-controls\">");
        if (!compact)
        {
            sb.Append("""
  <label class="lf-range" for="g3d-hops">Unfold hops
    <input type="range" id="g3d-hops" min="1" max="4" step="1" value="2">
    <span id="g3d-hops-val">2</span>
  </label>
  <label class="lf-range" for="g3d-spread">Spread
    <input type="range" id="g3d-spread" min="1" max="12" step="1" value="3" title="Constellation ⇄ tight cluster: node repulsion and link length">
    <span id="g3d-spread-val">3</span>
  </label>
  <label class="lf-check"><input type="checkbox" id="g3d-imports" checked> Show import edges</label>
  <label class="lf-check"><input type="checkbox" id="g3d-calls" checked> Show call edges</label>
""");
        }
        sb.Append("""
  <label class="lf-check"><input type="checkbox" id="g3d-hide-tests"> Hide test files</label>
""");
        if (!compact)
        {
            sb.Append("""
  <label class="lf-check"><input type="checkbox" id="g3d-hide-orphans"> Hide orphans</label>
  <label class="lf-check"><input type="checkbox" id="g3d-isolate"> Isolate focus</label>
  <button class="btn" id="g3d-freeze" type="button" title="Pin nodes in place (orbit still works); click again to re-settle">Freeze</button>
  <label class="lf-select" for="g3d-color">Colour by
    <select id="g3d-color">
      <option value="coupling" selected>Coupling (heat)</option>
      <option value="folder">Folder</option>
      <option value="type">File type</option>
    </select>
  </label>
  <label class="lf-select" for="g3d-degree-mode">Highlight
    <select id="g3d-degree-mode">
      <option value="off">— none —</option>
      <option value="ge">≥ connections</option>
      <option value="le">≤ connections</option>
    </select>
    <input type="number" id="g3d-degree-n" min="0" value="5" title="Connection-count threshold" disabled>
    <label class="lf-check"><input type="checkbox" id="g3d-degree-hide"> hide non-matches</label>
  </label>
  <label class="lf-search" for="g3d-filter">Filter
    <input type="search" id="g3d-filter" placeholder="path, folder or language…" autocomplete="off">
  </label>
""");
        }
        sb.Append("""
  <label class="lf-search" for="g3d-search">Find file
    <input type="search" id="g3d-search" list="g3d-search-list" placeholder="path or name… ( / )" autocomplete="off">
    <datalist id="g3d-search-list"></datalist>
  </label>
  <button class="btn" id="g3d-reset" type="button" title="Clear focus and show the whole graph">Reset view</button>
  <span class="filter-count" id="g3d-count"></span>
""");
        if (compact)
        {
            sb.Append($"<a class=\"btn\" href=\"{relRoot}graph.html\">Open full 3D graph →</a>");
        }
        sb.Append("</div>");

        // Canvas + side panel container. graph3d.js fills #graph3d-canvas.
        sb.Append($"""
<div id="graph3d-root" class="panel" style="padding:0;position:relative" data-compact="{(compact ? "1" : "0")}">
  <div id="graph3d-canvas" class="{canvasClass}"></div>
  <aside id="graph3d-panel" class="graph3d-panel" hidden></aside>
</div>
""");

        if (!compact)
        {
            // Legend (static, honest about the data channels).
            sb.Append("""
<details class="legend" open><summary>What the colours, sizes and edges mean</summary>
<div class="legend-grid">
  <span class="legend-item"><strong>Colour</strong> = folder, file type, or coupling heat (blue → red) — toggle above</span>
  <span class="legend-item"><strong>Size</strong> = coupling (fan-in + fan-out)</span>
  <span class="legend-item"><strong>Isolate focus</strong> = hide everything outside the focused file's neighbourhood</span>
  <span class="legend-item"><strong>Greyed node</strong> = automated-test file (toggle "Hide test files" to drop them)</span>
  <span class="legend-item"><span class="legend-swatch" style="background:#2f6fab;border-color:#2f6fab"></span> Blue edge = import / reference</span>
  <span class="legend-item"><span class="legend-swatch" style="background:#b7791f;border-color:#b7791f"></span> Amber edge = heuristic call (name + arity)</span>
  <span class="legend-item"><strong>Highlight</strong> = keep nodes with ≥/≤ N connections lit; dim the rest</span>
  <span class="legend-item"><strong>Click a node</strong> = focus + unfold its neighbours</span>
  <span class="legend-item"><strong>↯ Trace data flow</strong> = highlight everything reachable downstream (calls + imports), coloured by hop-distance</span>
  <span class="legend-item" id="g3d-type-legend" hidden></span>
</div>
<p class="note" style="margin:.6rem 0 0">Five <strong>data</strong> channels — position, colour, size, edge
style, and focus-distance animation — <strong>not</strong> five geometric axes. A screen shows three dimensions at most.</p>
</details>
""");
        }

        // Embed the graph payload inline so the viewer works from file:// (where
        // fetch() is blocked). model.json / graph.json remain for external tooling.
        // JsonSerializer's default encoder escapes < > & so this is safe inside <script>.
        sb.Append("<script>window.ARCH_GRAPH=").Append(GraphDataWriter.BuildJson(model)).Append(";</script>");

        // Load the vendored 3D bundle + controller (local paths, offline-safe).
        sb.Append($"<script src=\"{relRoot}assets/lib/3d-force-graph.min.js\"></script>");
        sb.Append($"<script src=\"{relRoot}assets/graph3d.js\"></script>");
        return sb.ToString();
    }
}
