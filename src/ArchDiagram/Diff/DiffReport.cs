using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Site;

namespace ArchDiagram.Diff;

/// <summary>Writes a single self-contained HTML report for a <see cref="ModelDiff.Result"/>,
/// reusing the same page shell and component set as the main site (no new CSS selectors).</summary>
public static class DiffReport
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Write(ModelDiff.Result diff, string outDir, string generatedOn)
    {
        Directory.CreateDirectory(outDir);
        SiteAssets.CopyTo(outDir);

        var body = Body(diff, generatedOn);
        var crumbs = PageTemplate.Crumbs((null, "Diff"));
        var html = PageTemplate.Render($"{diff.OldRootName} → {diff.NewRootName} — Diff", diff.NewRootName,
            "index.html", "", crumbs, body, navItems: [("index.html", "Diff", "⇄")]);

        var path = Path.Combine(outDir, "index.html");
        File.WriteAllText(path, html, Utf8NoBom);
        return path;
    }

    private static string Body(ModelDiff.Result diff, string generatedOn)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{Html.Encode(diff.OldRootName)} → {Html.Encode(diff.NewRootName)}</h1>");
        sb.Append($"<p class=\"lede\">Comparing two ArchDiagram model.json snapshots, generated {Html.Encode(generatedOn)}. "
            + "Files are matched by relative path (not slug, which can vary between independent scans). "
            + "This is a structural diff of the analysis — not a code diff.</p>");

        sb.Append("<div class=\"tiles\">");
        Tile(sb, diff.OldFileCount.ToString("N0"), "Files (old)");
        Tile(sb, diff.NewFileCount.ToString("N0"), "Files (new)");
        Tile(sb, diff.AddedFiles.Count.ToString("N0"), "Files added");
        Tile(sb, diff.RemovedFiles.Count.ToString("N0"), "Files removed");
        Tile(sb, diff.ChangedFiles.Count.ToString("N0"), "Files changed (LOC)");
        Tile(sb, diff.AddedDependencyEdges.Count.ToString("N0"), "Dep. edges added");
        Tile(sb, diff.RemovedDependencyEdges.Count.ToString("N0"), "Dep. edges removed");
        sb.Append("</div>");

        AppendScorecardChanges(sb, diff);
        AppendFileList(sb, "Added files", diff.AddedFiles, "warn");
        AppendFileList(sb, "Removed files", diff.RemovedFiles, "warn");
        AppendChangedFiles(sb, diff);
        AppendEdgeList(sb, "Dependency edges added", diff.AddedDependencyEdges);
        AppendEdgeList(sb, "Dependency edges removed", diff.RemovedDependencyEdges);

        return sb.ToString();
    }

    private static void AppendScorecardChanges(StringBuilder sb, ModelDiff.Result diff)
    {
        sb.Append($"<h2>Scorecard changes <span class=\"badge\">{diff.ScorecardChanges.Count}</span></h2>");
        if (diff.ScorecardChanges.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>No scorecard signal changed between the two snapshots.</p></div>");
            return;
        }
        sb.Append("<table class=\"grid\"><thead><tr><th>Signal</th><th>Old</th><th>New</th></tr></thead><tbody>");
        foreach (var c in diff.ScorecardChanges)
        {
            sb.Append($"<tr><td>{Html.Encode(c.Metric)}</td>"
                + $"<td>{Html.Encode(c.OldValue)} <span class=\"badge {StatusClass(c.OldStatus)}\">{c.OldStatus}</span></td>"
                + $"<td>{Html.Encode(c.NewValue)} <span class=\"badge {StatusClass(c.NewStatus)}\">{c.NewStatus}</span></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendFileList(StringBuilder sb, string title, List<string> files, string badgeClass)
    {
        sb.Append($"<h2>{Html.Encode(title)} <span class=\"badge {(files.Count > 0 ? badgeClass : "ok")}\">{files.Count}</span></h2>");
        if (files.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>None.</p></div>");
            return;
        }
        sb.Append("<div class=\"panel\"><ul class=\"member-list\">");
        foreach (var f in files.Take(500)) { sb.Append($"<li>{Html.Encode(f)}</li>"); }
        sb.Append("</ul></div>");
        if (files.Count > 500) { sb.Append($"<p class=\"note\">{files.Count - 500} more not shown.</p>"); }
    }

    private static void AppendChangedFiles(StringBuilder sb, ModelDiff.Result diff)
    {
        sb.Append($"<h2>Changed files (LOC) <span class=\"badge\">{diff.ChangedFiles.Count}</span></h2>");
        if (diff.ChangedFiles.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>No shared file's line count changed.</p></div>");
            return;
        }
        sb.Append("<table class=\"grid\"><thead><tr><th>File</th><th>Old LOC</th><th>New LOC</th><th>Δ</th></tr></thead><tbody>");
        foreach (var c in diff.ChangedFiles.Take(500))
        {
            var delta = c.NewLoc - c.OldLoc;
            var sign = delta > 0 ? "+" : "";
            sb.Append($"<tr><td>{Html.Encode(c.RelPath)}</td><td>{c.OldLoc:N0}</td><td>{c.NewLoc:N0}</td><td>{sign}{delta:N0}</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (diff.ChangedFiles.Count > 500) { sb.Append($"<p class=\"note\">{diff.ChangedFiles.Count - 500} more not shown.</p>"); }
    }

    private static void AppendEdgeList(StringBuilder sb, string title, List<string> edges)
    {
        sb.Append($"<h2>{Html.Encode(title)} <span class=\"badge\">{edges.Count}</span></h2>");
        if (edges.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div><p>None.</p></div>");
            return;
        }
        sb.Append("<div class=\"panel\"><ul class=\"member-list\">");
        foreach (var e in edges.Take(500)) { sb.Append($"<li><code>{Html.Encode(e)}</code></li>"); }
        sb.Append("</ul></div>");
        if (edges.Count > 500) { sb.Append($"<p class=\"note\">{edges.Count - 500} more not shown.</p>"); }
    }

    private static string StatusClass(ScorecardBuilder.Status s) => s switch
    {
        ScorecardBuilder.Status.Ok => "ok",
        ScorecardBuilder.Status.Watch => "",
        ScorecardBuilder.Status.Fail => "warn",
        _ => "",
    };

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
}
