using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>The prioritised refactoring backlog: every finding across the analyses, ranked by
/// severity, each with why it matters, a concrete tip, and a link to the detail. The single
/// "where do I start?" work-list for a brownfield codebase.</summary>
public static class RefactorPage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Refactoring Backlog</h1>");
        sb.Append("<p class=\"lede\">Everything the analyses flagged, in one prioritised list — what to fix, why it matters, "
                + "and a concrete first step. Heuristic and syntax-only: treat it as a ranked starting point for judgement, not a mandate.</p>");

        var items = RefactoringBacklog.Build(model);
        if (items.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">✓</div>"
                    + "<p>No refactoring findings surfaced — no cycles, drift, embedded secrets, poor-maintainability files or "
                    + "against-the-grain dependencies. Clean by these measures.</p></div>");
            return sb.ToString();
        }

        sb.Append("<div class=\"tiles\">");
        foreach (var (sev, label) in new[] { (RefactoringBacklog.Sev.Critical, "Critical"), (RefactoringBacklog.Sev.High, "High"),
                                             (RefactoringBacklog.Sev.Medium, "Medium"), (RefactoringBacklog.Sev.Low, "Low") })
        {
            var n = items.Count(i => i.Severity == sev);
            var warn = sev is RefactoringBacklog.Sev.Critical or RefactoringBacklog.Sev.High && n > 0;
            var cls = warn ? " style=\"border-color:var(--warn)\"" : "";
            sb.Append($"<div class=\"tile\"{cls}><div class=\"num\">{n}</div><div class=\"lbl\">{label}</div></div>");
        }
        sb.Append("</div>");

        sb.Append("<table class=\"grid\"><thead><tr><th>Priority</th><th>Area</th><th>Finding</th><th>Why</th><th>Suggested fix</th></tr></thead><tbody>");
        foreach (var i in items)
        {
            var (badge, cls) = i.Severity switch
            {
                RefactoringBacklog.Sev.Critical => ("critical", "warn"),
                RefactoringBacklog.Sev.High => ("high", "warn"),
                RefactoringBacklog.Sev.Medium => ("medium", ""),
                _ => ("low", ""),
            };
            var finding = i.Link.Length > 0
                ? $"<a href=\"{Html.Encode(i.Link)}\">{Html.Encode(i.Title)}</a>"
                : Html.Encode(i.Title);
            sb.Append($"<tr><td><span class=\"badge {cls}\">{badge}</span></td><td>{Html.Encode(i.Category)}</td>"
                    + $"<td>{finding}</td><td>{Html.Encode(i.Why)}</td><td>{Html.Encode(i.Tip)}</td></tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
