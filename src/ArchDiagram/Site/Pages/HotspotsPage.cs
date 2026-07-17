using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Metrics page: coupling hotspots (fan-in/fan-out), biggest files,
/// most-used external packages, and every TODO/FIXME marker found in comments.
/// Everything links back into the per-file pages.</summary>
public static class HotspotsPage
{
    public static string Body(ProjectModel model, bool showComplexity = false) =>
        Body(SiteContext.Build(model), showComplexity);

    /// <summary>Reuses the site-wide fan-in/fan-out/external indexes in <paramref name="ctx"/>
    /// instead of rescanning model.FileDependencies.</summary>
    public static string Body(SiteContext ctx, bool showComplexity = false)
    {
        var model = ctx.Model;
        var bySlug = ctx.BySlug;
        var fanIn = ctx.FanIn;
        var fanOut = ctx.FanOut;
        var external = ctx.ExternalCounts;

        // Marker headline counts are first-party (test files often embed marker-shaped strings
        // as fixture data — e.g. TodoScannerTests). Test markers still appear in the table,
        // tagged data-test so the 🧪 toggle reveals them.
        var todoFiles = model.Files.Where(f => f.Todos.Count > 0 && !f.IsVendored).ToList();
        var todoCount = todoFiles.Where(f => !f.IsTest).Sum(f => f.Todos.Count);
        var testTodoCount = todoFiles.Where(f => f.IsTest).Sum(f => f.Todos.Count);

        var sb = new StringBuilder();
        sb.Append("<h1>Hotspots &amp; Metrics</h1>");
        sb.Append("""
<p class="lede">Where the complexity and coupling live: the files everything depends on, the files
that reach out the most, the largest files, the external packages the codebase leans on, and every
TODO/FIXME left in comments. High fan-in files are risky to change; high fan-out files know too much.</p>
""");

        sb.Append("<div class=\"tiles\">");
        Tile(sb, fanIn.Count.ToString("N0"), "Depended-on files");
        Tile(sb, external.Count.ToString("N0"), "External packages");
        Tile(sb, todoCount.ToString("N0"), "TODO / FIXME markers");
        Tile(sb, model.Files.Count(f => f.Loc > 500 && !f.IsVendored).ToString("N0"), "Files over 500 LOC");
        if (showComplexity)
        {
            var complex = model.Files.SelectMany(f => f.Types).SelectMany(t => t.Methods)
                .Count(m => m.Cognitive >= Severity.SonarGate);
            Tile(sb, complex.ToString("N0"), $"Methods over cognitive {Severity.SonarGate}");
        }
        sb.Append("</div>");

        sb.Append("<div class=\"two-col\">");
        RankTable(sb, "Most depended-on <span class=\"badge\">fan-in</span> " + Glossary.Info("fan-in"),
            "Files many other files import. Changes here ripple widest.",
            fanIn, bySlug, "imported by");
        RankTable(sb, "Most dependencies <span class=\"badge\">fan-out</span> " + Glossary.Info("fan-out"),
            "Files that import the most other files in this codebase.",
            fanOut, bySlug, "imports");
        sb.Append("</div>");

        // Largest files. Vendored/minified bundles are excluded — a 3 MB library would
        // otherwise top this list and bury the project's own largest files.
        sb.Append("<h2>Largest files</h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Language</th><th>LOC</th><th>Size</th><th>Types</th><th>Methods</th></tr></thead><tbody>");
        foreach (var f in model.Files.Where(f => !f.IsVendored).OrderByDescending(f => f.Loc).Take(20))
        {
            sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath)}</a></td>" +
                      $"<td>{Html.Encode(f.Language)}</td><td>{f.Loc:N0}</td><td>{StructurePage.FormatBytes(f.SizeBytes)}</td>" +
                      $"<td>{f.Types.Count:N0}</td><td>{f.Types.Sum(t => t.Methods.Count):N0}</td></tr>");
        }
        sb.Append("</tbody></table>");

        AppendMaintainability(sb, model);

        if (showComplexity) { AppendMostComplex(sb, model); }

        // External packages.
        if (external.Count > 0)
        {
            sb.Append($"<h2>External packages &amp; namespaces <span class=\"badge\">{external.Count}</span></h2>");
            sb.Append("<p class=\"lede\">Imports that did not resolve to a file inside this codebase, ranked by how many files use them.</p>");
            sb.Append("<table class=\"grid\"><thead><tr><th>Package / namespace</th><th>Imported by</th></tr></thead><tbody>");
            foreach (var (name, count) in external.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(40))
            {
                sb.Append($"<tr><td><code>{Html.Encode(name)}</code></td><td>{count:N0} file(s)</td></tr>");
            }
            sb.Append("</tbody></table>");
            if (external.Count > 40) { sb.Append($"<p class=\"note\">{external.Count - 40} more in model.json.</p>"); }
        }

        AppendOrphans(sb, model);

        // TODOs.
        sb.Append($"<h2>TODO / FIXME markers <span class=\"badge {(todoCount > 0 ? "warn" : "ok")}\">{todoCount}</span></h2>");
        if (todoCount == 0 && testTodoCount == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>No TODO, FIXME, HACK or BUG markers were found in comments. Clean!</p></div>");
        }
        else
        {
            if (todoCount == 0)
            {
                sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>No TODO/FIXME markers in first-party code.</p></div>");
            }
            if (testTodoCount > 0)
            {
                sb.Append($"<p class=\"note\">{testTodoCount:N0} marker(s) are in test files (often fixture strings, not real work) — "
                        + "hidden by default; use the 🧪 Tests toggle to show them.</p>");
            }
            sb.Append("<table class=\"grid\"><thead><tr><th>Tag</th><th>Text</th><th>File</th><th>Line</th></tr></thead><tbody>");
            var shown = 0;
            foreach (var f in todoFiles.OrderBy(f => f.IsTest).ThenByDescending(f => f.Todos.Count))
            {
                foreach (var t in f.Todos)
                {
                    if (++shown > 200) { break; }
                    var cls = t.Tag is "FIXME" or "BUG" ? "warn" : "";
                    var attribution = t.Author.Length > 0 ? $" <span class=\"badge accent\">{Html.Encode(t.Author)}</span>" : "";
                    sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><span class=\"badge {cls}\">{Html.Encode(t.Tag)}</span></td><td>{Html.Encode(t.Text)}{attribution}</td>" +
                              $"<td><a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath)}</a></td><td>{t.Line}</td></tr>");
                }
                if (shown > 200) { break; }
            }
            sb.Append("</tbody></table>");
            if (todoCount > 200) { sb.Append($"<p class=\"note\">{todoCount - 200} more markers in model.json.</p>"); }
        }

        return sb.ToString();
    }

    /// <summary>Per-file maintainability proxy: a distribution across good/moderate/poor and the
    /// riskiest files with the drivers (size, complexity, coupling) that pull each score down.</summary>
    private static void AppendMaintainability(StringBuilder sb, ProjectModel model)
    {
        var scores = Analysis.MaintainabilityScorer.Rank(model);
        sb.Append("<h2>Maintainability " + Glossary.Info("maintainability") + "</h2>");
        if (scores.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">📊</div>"
                    + "<p>No first-party files with code to score.</p></div>");
            return;
        }
        var good = scores.Count(s => s.Band == Analysis.MaintainabilityScorer.Band.Good);
        var mod = scores.Count(s => s.Band == Analysis.MaintainabilityScorer.Band.Moderate);
        var poor = scores.Count(s => s.Band == Analysis.MaintainabilityScorer.Band.Poor);
        var total = scores.Count;
        sb.Append("<p class=\"lede\">A heuristic 0–100 score per file blending size, peak method complexity and coupling "
                + "(lower = riskier to change). A distribution, then the files most in need of attention.</p>");
        // Distribution bar (reuses the language-bar styling).
        sb.Append("<div class=\"lang-bar\">");
        void Seg(int n, string colour, string label)
        {
            if (n == 0) { return; }
            var pct = 100.0 * n / total;
            sb.Append($"<span style=\"width:{pct:F1}%;background:{colour}\" title=\"{Html.Encode(label)}: {n} file(s)\"></span>");
        }
        Seg(good, "var(--ok)", "Good (70–100)");
        Seg(mod, "var(--warn)", "Moderate (40–69)");
        Seg(poor, "var(--danger)", "Poor (0–39)");
        sb.Append("</div><div class=\"lang-legend\">"
                + $"<span><span class=\"lang-dot\" style=\"background:var(--ok)\"></span>Good {good}</span>"
                + $"<span><span class=\"lang-dot\" style=\"background:var(--warn)\"></span>Moderate {mod}</span>"
                + $"<span><span class=\"lang-dot\" style=\"background:var(--danger)\"></span>Poor {poor}</span></div>");

        var worst = scores.Where(s => s.Band != Analysis.MaintainabilityScorer.Band.Good).Take(20).ToList();
        if (worst.Count > 0)
        {
            sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Score</th><th>LOC</th>"
                    + "<th>Peak cognitive</th><th>Coupling</th></tr></thead><tbody>");
            foreach (var s in worst)
            {
                var cls = s.Band == Analysis.MaintainabilityScorer.Band.Poor ? "warn" : "";
                sb.Append($"<tr{(s.File.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{s.File.Slug}.html\">{Html.Encode(s.File.RelPath)}</a></td>"
                        + $"<td><span class=\"badge {cls}\">{s.Score}</span></td><td>{s.Loc:N0}</td>"
                        + $"<td>{s.MaxCognitive:N0}</td><td>{s.Coupling:N0}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        else
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>Every file scores in the healthy range.</p></div>");
        }
    }

    /// <summary>Ranks C# methods by cognitive then cyclomatic complexity and lists
    /// the top 25 with a severity band, linking each back to its file page.</summary>
    private static void AppendMostComplex(StringBuilder sb, ProjectModel model)
    {
        var methods = model.Files
            .SelectMany(f => f.Types.SelectMany(t => t.Methods.Select(m => (File: f, Type: t, Method: m))))
            .Where(x => x.Method.Cyclomatic > 1 || x.Method.Cognitive > 0)
            .OrderByDescending(x => x.Method.Cognitive).ThenByDescending(x => x.Method.Cyclomatic)
            .ToList();

        sb.Append("<h2>Most complex methods <span class=\"badge warn\">by cognitive score</span></h2>");
        sb.Append("""
<p class="lede">The methods hardest to read and test, ranked by SonarSource cognitive complexity
(how tangled the control flow is). Cyclomatic complexity — the number of independent paths through
the method — is shown alongside. High scores are prime candidates for refactoring or extra tests.
Only C# methods are scored.</p>
""");

        if (methods.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>" +
                      "<p>No C# methods with branching were analysed, so there is nothing to score for complexity yet.</p></div>");
            return;
        }

        sb.Append("<table class=\"grid\"><thead><tr><th>Method</th><th>File</th><th>Type</th>" +
                  $"<th>Cyclomatic {Glossary.Info("cyclomatic")}</th><th>Cognitive {Glossary.Info("cognitive")}</th><th>Level</th></tr></thead><tbody>");
        foreach (var (file, type, method) in methods.Take(25))
        {
            sb.Append($"<tr{(file.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{file.Slug}.html\">{Html.Encode(method.Name)}</a></td>" +
                      $"<td><a href=\"files/{file.Slug}.html\">{Html.Encode(file.RelPath)}</a></td>" +
                      $"<td>{Html.Encode(type.Name)}</td>" +
                      $"<td>{method.Cyclomatic:N0}</td><td>{method.Cognitive:N0}</td>" +
                      $"<td>{Severity.Badge(method.Cognitive)}</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (methods.Count > 25) { sb.Append($"<p class=\"note\">{methods.Count - 25} more scored methods in model.json.</p>"); }
    }

    /// <summary>Files with no incoming or outgoing internal import links — candidates for dead
    /// code (or genuinely standalone entry points/config). Only shown when the codebase has
    /// import links at all, so a language we don't resolve imports for doesn't flag everything.</summary>
    private static void AppendOrphans(StringBuilder sb, ProjectModel model)
    {
        if (model.FileDependencies.Count(e => e.ToSlug.Length > 0) == 0) { return; }

        var connected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies)
        {
            if (e.ToSlug.Length == 0) { continue; }
            connected.Add(e.FromSlug);
            connected.Add(e.ToSlug);
        }
        var orphans = model.Files
            .Where(f => !connected.Contains(f.Slug))
            .OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        sb.Append($"<h2>Unreferenced files <span class=\"badge\">{orphans.Count}</span></h2>");
        sb.Append("<p class=\"lede\">Files with no incoming or outgoing links in the internal import graph. "
                + "Many are legitimately standalone (entry points, config, docs), but this is where dead code hides.</p>");
        if (orphans.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>Every file is connected to at least one other. No obvious orphans.</p></div>");
            return;
        }
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Language</th><th>LOC</th><th>Purpose</th></tr></thead><tbody>");
        foreach (var f in orphans.Take(100))
        {
            sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath)}</a></td>" +
                      $"<td>{Html.Encode(f.Language)}</td><td>{f.Loc:N0}</td><td>{Html.Encode(f.Purpose)}</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (orphans.Count > 100) { sb.Append($"<p class=\"note\">{orphans.Count - 100} more in model.json.</p>"); }
    }

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");

    private static void RankTable(StringBuilder sb, string title, string lede,
        IReadOnlyDictionary<string, int> counts, IReadOnlyDictionary<string, FileNode> bySlug, string verb)
    {
        sb.Append($"<div><h2>{title}</h2><p class=\"lede\" style=\"font-size:.88rem\">{Html.Encode(lede)}</p>");
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Links</th></tr></thead><tbody>");
        foreach (var (slug, count) in counts.OrderByDescending(kv => kv.Value).Take(15))
        {
            var f = bySlug.GetValueOrDefault(slug);
            if (f is null) { continue; }
            sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{f.Slug}.html\" title=\"{Html.Encode(f.Purpose)}\">{Html.Encode(f.RelPath)}</a></td>" +
                      $"<td>{verb} {count:N0}</td></tr>");
        }
        sb.Append("</tbody></table></div>");
    }
}
