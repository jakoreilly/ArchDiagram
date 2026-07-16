using System.Text;
using System.Text.Json;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site;

public static class Html
{
    public static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);
}

/// <summary>Shared page shell: sidebar navigation, breadcrumbs, theme toggle,
/// local asset references only (works from file:// with no network).</summary>
public static class PageTemplate
{
    public static readonly (string Href, string Title, string Icon)[] Nav =
    [
        ("index.html", "Overview", "◈"),
        ("guide.html", "Guide", "❓"),
        ("structure.html", "Structure", "🗀"),
        ("dependencies.html", "Dependencies", "⇄"),
        ("modules.html", "Modules", "⬡"),
        ("layers.html", "Layering", "≡"),
        ("metrics.html", "Metrics", "📐"),
        ("scorecard.html", "Scorecard", "✔"),
        ("graph.html", "Graph (3D)", "🕸"),
        ("types.html", "Types & Members", "❖"),
        ("api.html", "API Surface", "⧉"),
        ("calls.html", "Call Graph", "☎"),
        ("packages.html", "Packages", "📦"),
        ("config.html", "Config & Secrets", "🔑"),
        ("hotspots.html", "Hotspots", "◉"),
    ];

    /// <summary>Sidebar navigation grouped into sections (order = display order). Keeps the
    /// flat <see cref="Nav"/> for callers/tests that just need the hrefs.</summary>
    public static readonly (string Section, (string Href, string Title, string Icon)[] Items)[] NavSections =
    [
        ("Start", [("index.html", "Overview", "◈"), ("guide.html", "Guide", "❓")]),
        ("Structure", [("structure.html", "Structure", "🗀"), ("dependencies.html", "Dependencies", "⇄"),
                       ("modules.html", "Modules", "⬡"), ("layers.html", "Layering", "≡"), ("graph.html", "Graph (3D)", "🕸")]),
        ("Health", [("scorecard.html", "Scorecard", "✔"), ("metrics.html", "Metrics", "📐"), ("hotspots.html", "Hotspots", "◉")]),
        ("Code", [("types.html", "Types & Members", "❖"), ("api.html", "API Surface", "⧉"), ("calls.html", "Call Graph", "☎")]),
        ("Supply chain", [("packages.html", "Packages", "📦"), ("config.html", "Config & Secrets", "🔑")]),
    ];

    /// <param name="relRoot">"" for root pages, "../" for pages under files/.</param>
    public static string Render(string title, string siteName, string activeHref, string relRoot, string breadcrumbsHtml, string bodyHtml,
        IReadOnlyList<(string Href, string Title, string Icon)>? navItems = null, SourceLink? sourceLink = null)
    {
        var nav = new StringBuilder();
        if (navItems is null)
        {
            // Grouped sidebar with section labels.
            foreach (var (section, items) in NavSections)
            {
                nav.Append($"<div class=\"nav-section\">{Html.Encode(section)}</div>\n");
                foreach (var (href, navTitle, icon) in items)
                {
                    var active = href == activeHref ? " class=\"active\"" : "";
                    nav.Append($"<a href=\"{relRoot}{href}\"{active}><span class=\"nav-icon\">{icon}</span>{Html.Encode(navTitle)}</a>\n");
                }
            }
        }
        else
        {
            foreach (var (href, navTitle, icon) in navItems)
            {
                var active = href == activeHref ? " class=\"active\"" : "";
                nav.Append($"<a href=\"{relRoot}{href}\"{active}><span class=\"nav-icon\">{icon}</span>{Html.Encode(navTitle)}</a>\n");
            }
        }

        // Source-link config for the client-side linker (sourcelink.js). null =
        // not configured; the viewer then offers an in-browser prompt instead.
        var sourceLinkScript = sourceLink is null
            ? ""
            : $"<script>window.ARCH_SOURCELINK={JsonSerializer.Serialize(sourceLink, ModelJsonWriter.Options)};</script>\n";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Html.Encode(title)}} — {{Html.Encode(siteName)}}</title>
<link rel="stylesheet" href="{{relRoot}}assets/site.css">
<script>
// Apply saved theme before first paint to avoid a flash.
(function () {
  var t = null;
  try { t = localStorage.getItem("archdiagram-theme"); } catch (e) { }
  if (!t) { t = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"; }
  document.documentElement.setAttribute("data-theme", t);
  // Test files are hidden by default; apply the class pre-paint to avoid a flash.
  var showTests = null;
  try { showTests = localStorage.getItem("archdiagram-show-tests"); } catch (e) { }
  if (showTests !== "1") { document.documentElement.classList.add("hide-tests"); }
})();
</script>
</head>
<body>
<div class="layout">
  <aside class="sidebar">
    <div class="brand"><span class="brand-mark">◆</span><div><div class="brand-name">ArchDiagram</div><div class="brand-sub">{{Html.Encode(siteName)}}</div></div></div>
    <button class="btn search-open" id="search-open" type="button" title="Search files, types and methods (Ctrl+K)">🔍 Search <kbd>Ctrl K</kbd></button>
    <nav>
{{nav}}    </nav>
    <div class="sidebar-foot">
      <button class="btn theme-toggle" id="theme-toggle" type="button" title="Switch between light and dark theme">◐ Theme</button>
      <button class="btn" id="tests-toggle" type="button" title="Show or hide test files across the site">🧪 Tests: hidden</button>
    </div>
  </aside>
  <main class="content">
    <div class="breadcrumbs">{{breadcrumbsHtml}}</div>
{{bodyHtml}}
  </main>
</div>
<div class="hover-tip" id="hover-tip" hidden></div>
<div class="explain-pop" id="explain-pop" hidden role="dialog" aria-label="Explanation"></div>
<script type="application/json" id="arch-glossary">{{Glossary.Json()}}</script>
<div class="palette-overlay" id="palette" hidden data-rel-root="{{relRoot}}">
  <div class="palette">
    <input type="text" id="palette-input" placeholder="Search files, types, methods…" autocomplete="off" spellcheck="false">
    <ul class="palette-results" id="palette-results"></ul>
    <div class="palette-foot">↑↓ navigate · Enter open · Esc close</div>
  </div>
</div>
<script src="{{relRoot}}assets/lib/mermaid.min.js"></script>
<script src="{{relRoot}}assets/search-index.js"></script>
{{sourceLinkScript}}<script src="{{relRoot}}assets/sourcelink.js"></script>
<script src="{{relRoot}}assets/site.js"></script>
</body>
</html>
""";
    }

    /// <summary>One interactive diagram card: toolbar (zoom/reset/PNG), pan/zoom stage,
    /// the mermaid source, and the alias->tooltip map site.js turns into hover cards.</summary>
    public static string DiagramBlock(string id, Diagram diagram, string pngName, bool hidden = false, string group = "", bool deferred = false)
    {
        var tooltipJson = JsonSerializer.Serialize(diagram.Tooltips);
        var hrefJson = JsonSerializer.Serialize(diagram.Hrefs);
        var hiddenAttr = hidden ? " hidden" : "";
        var groupAttr = group.Length > 0 ? $" data-group=\"{Html.Encode(group)}\"" : "";
        var deferredAttr = deferred ? " data-deferred=\"1\"" : "";
        // Trim banner: this diagram was capped for readability. Be explicit about how
        // many nodes were collapsed so a truncated view never reads as "everything".
        var trimBanner = diagram.Trimmed
            ? $"<p class=\"note diagram-trim\">Showing the <strong>{diagram.ShownNodes:N0}</strong> most-connected of "
              + $"<strong>{diagram.TotalNodes:N0}</strong> nodes — the rest were collapsed into the dashed "
              + "“… and N more” node to keep this diagram readable. Open the file pages or the 3D graph for the full set.</p>"
            : "";
        return $"""
<div class="diagram-card" id="{Html.Encode(id)}" data-png-name="{Html.Encode(pngName)}"{groupAttr}{hiddenAttr}{deferredAttr}>
  {trimBanner}<div class="toolbar">
    <button class="btn" data-act="zoom-in" type="button" title="Zoom in">+</button>
    <button class="btn" data-act="zoom-out" type="button" title="Zoom out">&minus;</button>
    <button class="btn" data-act="zoom-reset" type="button" title="Reset view">Reset</button>
    <button class="btn" data-act="fit" type="button" title="Fit diagram to the visible area">Fit</button>
    <button class="btn btn-primary" data-act="png" type="button" title="Download this diagram as a PNG image">⬇ PNG</button>
    <button class="btn" data-act="svg" type="button" title="Download this diagram as a scalable SVG">⬇ SVG</button>
    <button class="btn" data-act="copy" type="button" title="Copy the Mermaid source of this diagram to the clipboard">Copy Mermaid</button>
    <span class="tb-hint">Scroll to zoom · drag to pan · hover for details · click a node to open it</span>
  </div>
  <div class="stage"><pre class="mermaid-src" hidden>{Html.Encode(diagram.Mermaid)}</pre><div class="render-target"></div></div>
  <script type="application/json" class="tooltips">{tooltipJson}</script>
  <script type="application/json" class="hrefs">{hrefJson}</script>
</div>
""";
    }

    /// <summary>Collapsible legend describing the node shapes/colours and edge styles
    /// used across all diagrams. Swatch fills mirror MermaidRenderer.ClassDefs so the
    /// legend always matches the rendered nodes. <paramref name="open"/> expands it
    /// (used on the Guide page where it is the primary content).</summary>
    public static string Legend(bool open = false)
    {
        var openAttr = open ? " open" : "";
        return $$"""
<details class="legend"{{openAttr}}><summary>What the shapes and colours mean</summary>
<div class="legend-grid">
  <span class="legend-item"><span class="legend-swatch" style="background:#dcecf9;border-color:#2f6fab"></span>Project / type</span>
  <span class="legend-item"><span class="legend-swatch db" style="background:#e8e3f5;border-color:#6b46c1"></span>Database</span>
  <span class="legend-item"><span class="legend-swatch" style="background:#e3f2e6;border-color:#2e7d32"></span>File in this codebase</span>
  <span class="legend-item"><span class="legend-swatch hex" style="background:#f0f0f0;border-color:#8a8a8a"></span>External package / namespace</span>
  <span class="legend-item"><span class="legend-swatch round" style="background:#fdf1dc;border-color:#b7791f"></span>Folder</span>
  <span class="legend-item"><span class="legend-line"></span>Import / reference (solid)</span>
  <span class="legend-item"><span class="legend-line dashed"></span>Heuristic / ambiguous link (dashed)</span>
</div>
<p class="note" style="margin:.6rem 0 0">Click any node with a page to open it. Grey dashed nodes live outside this codebase.</p>
</details>
""";
    }

    /// <summary>A note pointing at the interactive 3D graph, for pages whose static
    /// diagrams are capped. Root-level pages only (links to "graph.html").</summary>
    public static string ExploreIn3DNote() =>
        "<p class=\"note\">These diagrams are capped for readability. To explore <em>every</em> file at once — "
        + "with search, focus/unfold and a spread slider — open the <a href=\"graph.html\">3D Dependency Graph</a>.</p>";

    public static string Crumbs(params (string? Href, string Text)[] parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) { sb.Append(" <span class=\"crumb-sep\">/</span> "); }
            var (href, text) = parts[i];
            sb.Append(href is null
                ? $"<span class=\"crumb-here\">{Html.Encode(text)}</span>"
                : $"<a href=\"{href}\">{Html.Encode(text)}</a>");
        }
        return sb.ToString();
    }
}
