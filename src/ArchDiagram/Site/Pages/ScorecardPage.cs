using System.Text;
using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>A one-page architecture scorecard: headline signals each graded ok / watch / fail
/// against fixed thresholds, with an overall grade. The artifact to bring to an architecture
/// review. Built by <see cref="ScorecardBuilder"/> so it matches the Markdown export exactly.</summary>
public static class ScorecardPage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Architecture Scorecard</h1>");

        var card = ScorecardBuilder.Build(model);
        var (label, cls) = Grade(card.Overall);

        sb.Append("<p class=\"lede\">A quick, honest health check: each signal is graded against a fixed threshold. "
                + "Grades are heuristic and syntax-only — a conversation starter for a review, not a certification.</p>");

        sb.Append("<div class=\"tiles\"><div class=\"tile\" style=\"border-color:var(--" + cls + ")\">"
                + $"<div class=\"num\" style=\"color:var(--{cls})\">{label}</div><div class=\"lbl\">Overall grade</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{card.Rows.Count(r => r.Status == ScorecardBuilder.Status.Ok)}</div><div class=\"lbl\">Signals passing</div></div>");
        sb.Append($"<div class=\"tile\"><div class=\"num\">{card.Rows.Count(r => r.Status is ScorecardBuilder.Status.Watch or ScorecardBuilder.Status.Fail)}</div><div class=\"lbl\">Need attention</div></div>");
        sb.Append("</div>");

        sb.Append("<table class=\"grid\"><thead><tr><th>Signal</th><th>Value</th><th>Grade</th><th>What it means</th></tr></thead><tbody>");
        foreach (var r in card.Rows)
        {
            var (rl, rc) = GradeRow(r.Status);
            sb.Append($"<tr><td>{Html.Encode(r.Metric)}</td><td>{Html.Encode(r.Value)}</td>"
                    + $"<td><span class=\"badge {rc}\">{rl}</span></td><td>{Html.Encode(r.Note)}</td></tr>");
        }
        sb.Append("</tbody></table>");

        sb.Append("<details class=\"legend\"><summary>How each signal is graded</summary>"
                + "<div class=\"legend-grid\" style=\"flex-direction:column;gap:.35rem\">"
                + "<span class=\"legend-item\"><strong>Dependency cycles</strong> — 0 = pass; any = fail.</span>"
                + "<span class=\"legend-item\"><strong>Propagation cost</strong> — ≤30% pass, ≤60% watch, else fail.</span>"
                + "<span class=\"legend-item\"><strong>Worst distance (D)</strong> — ≤0.30 pass, ≤0.60 watch, else fail (benign leaves excluded).</span>"
                + "<span class=\"legend-item\"><strong>Layering violations</strong> — 0 = pass; needs a declared contract, else n/a.</span>"
                + "<span class=\"legend-item\"><strong>Test-code ratio</strong> — ≥25% pass, ≥10% watch, else fail (proxy, not execution coverage).</span>"
                + "<span class=\"legend-item\"><strong>Package version drift</strong> — 0 = pass, else watch.</span>"
                + "<span class=\"legend-item\"><strong>Credentials in source</strong> — 0 = pass; any = fail.</span>"
                + "<span class=\"legend-item\"><strong>TODO / FIXME</strong> — ≤10 pass, ≤50 watch, else fail.</span>"
                + "</div></details>");

        sb.Append("<p class=\"note\">The same scorecard is written to <code>ARCHITECTURE.md</code> so it travels with the repo.</p>");
        return sb.ToString();
    }

    private static (string Label, string Cls) Grade(ScorecardBuilder.Status s) => s switch
    {
        ScorecardBuilder.Status.Ok => ("PASS", "ok"),
        ScorecardBuilder.Status.Watch => ("WATCH", "warn"),
        ScorecardBuilder.Status.Fail => ("AT RISK", "danger"),
        _ => ("—", "border"),
    };

    private static (string Label, string Cls) GradeRow(ScorecardBuilder.Status s) => s switch
    {
        ScorecardBuilder.Status.Ok => ("pass", "ok"),
        ScorecardBuilder.Status.Watch => ("watch", "warn"),
        ScorecardBuilder.Status.Fail => ("fail", "warn"),
        _ => ("n/a", ""),
    };
}
