using System.Globalization;
using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Evolution page: how the code has *changed* over its git history. Two lenses on the
/// per-file churn/ownership facts gathered in <see cref="GitHistory"/>: a "crime-scene" quadrant
/// (change frequency × complexity — the top-right corner is where cost and bugs concentrate), and
/// a bus-factor ownership table (files a single author knows). Server-rendered, heuristic, and
/// self-suppressing: when the scan wasn't a git working copy the whole page is a friendly
/// empty-state and the rest of the site is unaffected.</summary>
public static class EvolutionPage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Evolution</h1>");

        if (model.Git is null || !model.Git.Available)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🕓</div>"
                    + "<p>No git history was found for this scan, so churn and ownership analysis is unavailable. "
                    + "Run ArchDiagram against a full git working tree (not a dropped-in folder or a "
                    + "<code>--from-model</code> rebuild) to see which files change most and who owns them.</p></div>");
            return sb.ToString();
        }

        sb.Append("<p class=\"lede\">How this codebase changes over time, from its git history. "
                + "Files that are <strong>both</strong> frequently changed and complex are where cost and "
                + "defects concentrate — the top-right of the chart. Ownership shows where knowledge is "
                + "concentrated in a single person (a “bus factor” risk).</p>");
        sb.Append("<p class=\"note\">Heuristic: churn counts commits that touched each file (renames count as "
                + "separate paths); complexity is peak cognitive complexity, so non-C# files sit on the zero line "
                + "and are ranked by churn alone. Authorship is by commit count, using git author names as-is.</p>");
        if (model.Git.Shallow)
        {
            sb.Append("<p class=\"note diagram-trim\">This is a <strong>shallow clone</strong> — commit counts "
                    + "undercount real history. Clone with full history for accurate churn.</p>");
        }

        // First-party files that git actually tracked (commitCount > 0).
        var tracked = model.Files
            .Where(f => CodebaseStats.IsFirstParty(f) && f.CommitCount > 0)
            .ToList();

        var busFactorOne = tracked.Count(f => f.AuthorCount == 1 && f.CommitCount >= BusFactorMinCommits);
        var maxChurn = tracked.Count > 0 ? tracked.Max(f => f.CommitCount) : 0;
        var maxCog = tracked.Count > 0 ? tracked.Max(PeakCognitive) : 0;
        var hotspots = RankHotspots(tracked, maxChurn, maxCog);

        sb.Append("<div class=\"tiles\">");
        Tile(sb, model.Git.TotalCommits.ToString("N0"), "Commits in history");
        Tile(sb, tracked.Count.ToString("N0"), "Tracked first-party files");
        Tile(sb, hotspots.Count(h => h.Hot).ToString("N0"), "Change hotspots", hotspots.Any(h => h.Hot));
        Tile(sb, busFactorOne.ToString("N0"), "Single-author files", busFactorOne > 0);
        sb.Append("</div>");

        if (tracked.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>Git history is present but no first-party files carry commits yet.</p></div>");
            return sb.ToString();
        }

        AppendQuadrant(sb, hotspots, maxChurn, maxCog);
        AppendHotspotTable(sb, hotspots);
        AppendOwnership(sb, tracked);
        return sb.ToString();
    }

    // A file needs at least this many commits before single-authorship reads as a real
    // knowledge-concentration risk rather than a brand-new file only its creator has touched.
    private const int BusFactorMinCommits = 5;

    private sealed record Hotspot(FileNode File, int Churn, int Cognitive, double Score, bool Hot);

    private static int PeakCognitive(FileNode f) =>
        f.Types.SelectMany(t => t.Methods).Select(m => m.Cognitive).DefaultIfEmpty(0).Max();

    /// <summary>Hotspot score: the product of churn and complexity, each normalised to its own max
    /// so neither axis dominates. "Hot" = in the top-right quadrant (both above half of their max).
    /// Ordered by score desc, then path for determinism.</summary>
    private static List<Hotspot> RankHotspots(List<FileNode> tracked, int maxChurn, int maxCog)
    {
        double NChurn(int c) => maxChurn == 0 ? 0 : (double)c / maxChurn;
        double NCog(int c) => maxCog == 0 ? 0 : (double)c / maxCog;
        return tracked
            .Select(f =>
            {
                var churn = f.CommitCount;
                var cog = PeakCognitive(f);
                var hot = NChurn(churn) > 0.5 && NCog(cog) > 0.5;
                return new Hotspot(f, churn, cog, NChurn(churn) * NCog(cog), hot);
            })
            .OrderByDescending(h => h.Score).ThenBy(h => h.File.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---- Churn × complexity quadrant (SVG; mirrors MetricsPage's scatter idiom) ----

    private const double W = 480, H = 480, Pad = 44;

    private static void AppendQuadrant(StringBuilder sb, List<Hotspot> hotspots, int maxChurn, int maxCog)
    {
        sb.Append("<h2>Change hotspots <span class=\"badge\">churn × complexity</span></h2>");
        sb.Append("<p class=\"lede\">Each dot is a file at (commits, peak complexity). Top-right = changed often "
                + "AND complex — the prime refactoring/extra-test targets. Dot size = commits; colour flags the "
                + "top-right quadrant.</p>");
        sb.Append("<div class=\"metrics-scatter\">");
        sb.Append(CultureInfo.InvariantCulture, $"<svg viewBox=\"0 0 {W:0} {H:0}\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"Churn versus complexity scatter of files\">");
        // Border + quadrant split at the 50% lines.
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"{MapX(0, maxChurn):0.#}\" y=\"{MapY(1, 1):0.#}\" width=\"{W - 2 * Pad:0.#}\" height=\"{H - 2 * Pad:0.#}\" fill=\"none\" stroke=\"var(--border)\" stroke-width=\"1\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{MapX(maxChurn / 2.0, maxChurn):0.#}\" y1=\"{MapY(1, 1):0.#}\" x2=\"{MapX(maxChurn / 2.0, maxChurn):0.#}\" y2=\"{MapY(0, 1):0.#}\" stroke=\"var(--border)\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<line x1=\"{MapX(0, maxChurn):0.#}\" y1=\"{MapY(0.5, 1):0.#}\" x2=\"{MapX(maxChurn, maxChurn):0.#}\" y2=\"{MapY(0.5, 1):0.#}\" stroke=\"var(--border)\" stroke-width=\"1\" stroke-dasharray=\"3 3\"/>");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"{W / 2:0.#}\" y=\"{H - 8:0.#}\" font-size=\"12\" text-anchor=\"middle\" fill=\"var(--text-soft)\">Commits (churn) →</text>");
        sb.Append(CultureInfo.InvariantCulture, $"<text x=\"14\" y=\"{H / 2:0.#}\" font-size=\"12\" text-anchor=\"middle\" fill=\"var(--text-soft)\" transform=\"rotate(-90 14 {H / 2:0.#})\">Peak complexity →</text>");

        foreach (var h in hotspots.OrderBy(h => h.File.RelPath, StringComparer.Ordinal))
        {
            var cx = MapX(h.Churn, maxChurn);
            var cy = MapY(maxCog == 0 ? 0 : (double)h.Cognitive / maxCog, 1);
            var radius = 4 + 6.0 * (maxChurn == 0 ? 0 : (double)h.Churn / maxChurn);
            var fill = h.Hot ? "var(--danger)" : "var(--accent)";
            var title = $"{h.File.RelPath} · {h.Churn} commit(s), peak cognitive {h.Cognitive}"
                      + (h.File.AuthorCount > 0 ? $" · {h.File.AuthorCount} author(s)" : "");
            var enc = Html.Encode(title);
            sb.Append(CultureInfo.InvariantCulture,
                $"<circle cx=\"{cx:0.#}\" cy=\"{cy:0.#}\" r=\"{radius:0.#}\" fill=\"{fill}\" fill-opacity=\"0.75\" stroke=\"var(--bg-panel)\" stroke-width=\"1\" tabindex=\"0\" role=\"img\" data-tip=\"{enc}\" aria-label=\"{enc}\"><title>{enc}</title></circle>");
        }
        sb.Append("</svg></div>");
    }

    private static double MapX(double churn, int maxChurn) => Pad + (maxChurn == 0 ? 0 : churn / maxChurn) * (W - 2 * Pad);
    private static double MapY(double norm, int _) => (H - Pad) - norm * (H - 2 * Pad);

    private static void AppendHotspotTable(StringBuilder sb, List<Hotspot> hotspots)
    {
        var top = hotspots.Where(h => h.Churn > 1).Take(25).ToList();
        if (top.Count == 0) { return; }
        sb.Append("<h2>Most-changed files</h2>");
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Commits</th><th>Peak cognitive</th>"
                + "<th>Authors</th><th>Last changed</th></tr></thead><tbody>");
        foreach (var h in top)
        {
            var cls = h.Hot ? "warn" : "";
            sb.Append($"<tr{(h.File.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{h.File.Slug}.html\">{Html.Encode(h.File.RelPath)}</a>"
                    + (h.Hot ? " <span class=\"badge warn\">hotspot</span>" : "") + "</td>"
                    + $"<td><span class=\"badge {cls}\">{h.Churn:N0}</span></td><td>{h.Cognitive:N0}</td>"
                    + $"<td>{h.File.AuthorCount:N0}</td><td>{Html.Encode(h.File.LastModified)}</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendOwnership(StringBuilder sb, List<FileNode> tracked)
    {
        var solo = tracked
            .Where(f => f.AuthorCount == 1 && f.CommitCount >= BusFactorMinCommits)
            .OrderByDescending(f => f.CommitCount).ThenBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase)
            .Take(30).ToList();
        sb.Append($"<h2>Knowledge concentration <span class=\"badge {(solo.Count > 0 ? "warn" : "ok")}\">bus factor 1</span></h2>");
        if (solo.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + $"<p>No first-party file with {BusFactorMinCommits}+ commits is owned by a single author. "
                    + "Knowledge is shared.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Files with real history (" + BusFactorMinCommits + "+ commits) that only one "
                + "person has ever touched — if they leave, that knowledge leaves with them. Good candidates for a "
                + "review, a second pair of eyes, or documentation.</p>");
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Sole author</th><th>Commits</th>"
                + "<th>Last changed</th></tr></thead><tbody>");
        foreach (var f in solo)
        {
            sb.Append($"<tr{(f.IsTest ? " data-test=\"1\"" : "")}><td><a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath)}</a></td>"
                    + $"<td>{Html.Encode(f.PrincipalAuthor)}</td><td>{f.CommitCount:N0}</td>"
                    + $"<td>{Html.Encode(f.LastModified)}</td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void Tile(StringBuilder sb, string num, string label, bool warn = false)
    {
        var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
        sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
    }
}
