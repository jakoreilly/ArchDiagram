using System.Globalization;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Rolls the individual analyses up into a one-page architecture scorecard: a handful of
/// headline signals, each graded ok / watch / fail against a fixed threshold, plus an overall
/// grade (the worst row). Pure and deterministic; reused by the Scorecard page and the Markdown
/// export so the site and the doc never disagree.</summary>
public static class ScorecardBuilder
{
    public enum Status { Ok, Watch, Fail, NA }

    public sealed record Row(string Metric, string Value, Status Status, string Note);
    public sealed record Card(IReadOnlyList<Row> Rows, Status Overall);

    public static Card Build(ProjectModel model)
    {
        var rows = new List<Row>();
        var m = ArchitectureMetrics.Compute(model);

        rows.Add(new Row("Dependency cycles", m.Cycles.Count.ToString("N0"),
            m.Cycles.Count == 0 ? Status.Ok : Status.Fail,
            m.Cycles.Count == 0 ? "Dependencies flow one way." : "Modules depend on each other — break the cycle."));

        var pc = m.PropagationCost;
        rows.Add(new Row("Propagation cost", pc.ToString("P0", CultureInfo.InvariantCulture),
            pc <= 0.30 ? Status.Ok : pc <= 0.60 ? Status.Watch : Status.Fail,
            "Share of the module graph a change can reach; lower is looser coupling."));

        if (m.Modules.Count >= 2)
        {
            var ranked = m.Modules
                .Where(x => ArchitectureMetrics.Classify(x.Instability, x.Abstractness, x.Ca) != ArchitectureMetrics.Zone.BenignLeaf)
                .ToList();
            var worst = (ranked.Count > 0 ? ranked : m.Modules)[0];
            rows.Add(new Row("Worst distance (D)", worst.Distance.ToString("F2", CultureInfo.InvariantCulture),
                worst.Distance <= 0.3 ? Status.Ok : worst.Distance <= 0.6 ? Status.Watch : Status.Fail,
                $"Furthest module from the main sequence: {worst.Key}."));
        }

        var layering = LayeringAnalyzer.Analyze(model);
        rows.Add(layering.Declared
            ? new Row("Layering violations", layering.Violations.Count.ToString("N0"),
                layering.Violations.Count == 0 ? Status.Ok : Status.Fail,
                layering.Violations.Count == 0 ? "All dependencies flow downward." : "Upward dependencies break the declared contract.")
            : new Row("Layering violations", "n/a", Status.NA,
                "No layers contract declared (add archdiagram.layers.json to enable this check)."));

        var firstParty = CodebaseStats.FirstPartyLoc(model);
        var testLoc = CodebaseStats.TestLoc(model);
        var ratio = firstParty + testLoc == 0 ? 0.0 : (double)testLoc / (firstParty + testLoc);
        rows.Add(new Row("Test-code ratio", ratio.ToString("P0", CultureInfo.InvariantCulture),
            ratio >= 0.25 ? Status.Ok : ratio >= 0.10 ? Status.Watch : Status.Fail,
            "Test lines as a share of first-party + test lines (a rough coverage proxy, not execution coverage)."));

        var driftCount = model.Projects
            .SelectMany(p => p.Packages)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Select(x => x.Version).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
        rows.Add(new Row("Package version drift", driftCount.ToString("N0"),
            driftCount == 0 ? Status.Ok : Status.Watch,
            "Packages referenced at more than one version across projects."));

        var secrets = model.Projects.SelectMany(p => p.ConnectionStrings).Count(u => u.HasCredential);
        rows.Add(new Row("Credentials in source", secrets.ToString("N0"),
            secrets == 0 ? Status.Ok : Status.Fail,
            "Connection strings that embed a username/password committed to source."));

        var todos = model.Files.Where(CodebaseStats.IsFirstParty).Sum(f => f.Todos.Count);
        rows.Add(new Row("TODO / FIXME markers", todos.ToString("N0"),
            todos <= 10 ? Status.Ok : todos <= 50 ? Status.Watch : Status.Fail,
            "Open markers left in first-party comments."));

        // Overall = worst status present (NA never worsens the grade).
        var overall = rows.Select(r => r.Status).Where(s => s != Status.NA).DefaultIfEmpty(Status.Ok).Max();
        return new Card(rows, overall);
    }
}
