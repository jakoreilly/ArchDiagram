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

    public sealed record Row(string Metric, string Value, Status Status, string Note, string Action = "", string Link = "");
    public sealed record Card(IReadOnlyList<Row> Rows, Status Overall);

    public static Card Build(ProjectModel model)
    {
        var m = ArchitectureMetrics.Compute(model);
        var rows = new List<Row> { BuildCyclesRow(m), BuildPropagationCostRow(m) };
        var worstDistance = BuildWorstDistanceRow(m);
        if (worstDistance is not null) { rows.Add(worstDistance); }
        rows.Add(BuildLayeringRow(model));
        rows.Add(BuildTestRatioRow(model));
        rows.Add(BuildDriftRow(model));
        rows.Add(BuildSecretsRow(model));
        rows.Add(BuildTodosRow(model));

        // Overall = worst status present (NA never worsens the grade).
        var overall = rows.Select(r => r.Status).Where(s => s != Status.NA).DefaultIfEmpty(Status.Ok).Max();
        return new Card(rows, overall);
    }

    private static Row BuildCyclesRow(ArchitectureMetrics.Result m) =>
        new("Dependency cycles", m.Cycles.Count.ToString("N0"),
            m.Cycles.Count == 0 ? Status.Ok : Status.Fail,
            m.Cycles.Count == 0 ? "Dependencies flow one way." : "Modules transitively depend on each other, so none can be built, tested or understood in isolation.",
            m.Cycles.Count == 0 ? "" : "Break the cycle: introduce an interface owned by one module and depend on it, or move the shared type down into a lower module.",
            "metrics.html");

    // Propagation cost is only meaningful once there are a few modules to propagate across;
    // a 1–2 module graph is trivially ~100%, which would be a false alarm.
    private static Row BuildPropagationCostRow(ArchitectureMetrics.Result m)
    {
        var pc = m.PropagationCost;
        return m.Modules.Count < 3
            ? new Row("Propagation cost", "n/a", Status.NA,
                "Too few modules for this to be meaningful (a tiny graph is trivially high).")
            : new Row("Propagation cost", pc.ToString("P0", CultureInfo.InvariantCulture),
                pc <= 0.30 ? Status.Ok : pc <= 0.60 ? Status.Watch : Status.Fail,
                "Share of the module graph an average change can transitively reach; lower means looser coupling.",
                pc <= 0.30 ? "" : "Reduce cross-module coupling: split hub modules and depend on abstractions rather than concretes. See the Modules coupling matrix.",
                "modules.html");
    }

    // Worst distance needs abstractness to be meaningful. With no interfaces/abstract types
    // anywhere, D collapses to |Instability − 1| and simply flags concrete leaf/stable
    // modules — an artifact, not a defect. Report it as N/A with the reason rather than fail.
    // Returns null when there are fewer than 2 modules (nothing to rank).
    private static Row? BuildWorstDistanceRow(ArchitectureMetrics.Result m)
    {
        var maxAbstractness = m.Modules.Count > 0 ? m.Modules.Max(x => x.Abstractness) : 0.0;
        if (m.Modules.Count >= 2 && maxAbstractness > 0)
        {
            var ranked = m.Modules
                .Where(x => ArchitectureMetrics.Classify(x.Instability, x.Abstractness, x.Ca, x.IsPureData) != ArchitectureMetrics.Zone.BenignLeaf)
                .ToList();
            var worst = (ranked.Count > 0 ? ranked : m.Modules)[0];
            return new Row("Worst distance (D)", worst.Distance.ToString("F2", CultureInfo.InvariantCulture),
                worst.Distance <= 0.3 ? Status.Ok : worst.Distance <= 0.6 ? Status.Watch : Status.Fail,
                $"Furthest module from the ideal abstractness/stability balance: {worst.Key}.",
                worst.Distance <= 0.3 ? "" : $"If {worst.Key} is stable and heavily depended-on, introduce interfaces so dependents rely on contracts; if it is a leaf, this is acceptable.",
                "metrics.html");
        }
        if (m.Modules.Count >= 2)
        {
            return new Row("Worst distance (D)", "n/a", Status.NA,
                "No interfaces or abstract types were detected, so abstractness is 0 everywhere and this distance metric cannot discriminate — it would only restate instability. Treat as informational.",
                "Introduce interfaces/abstract base types for the stable, heavily-depended-on modules to make this metric meaningful.",
                "metrics.html");
        }
        return null;
    }

    private static Row BuildLayeringRow(ProjectModel model)
    {
        var layering = LayeringAnalyzer.Analyze(model);
        return layering.Declared
            ? new Row("Layering violations", layering.Violations.Count.ToString("N0"),
                layering.Violations.Count == 0 ? Status.Ok : Status.Fail,
                layering.Violations.Count == 0 ? "All dependencies flow downward through the declared layers." : "One or more dependencies point upward, breaking the declared layering contract.",
                layering.Violations.Count == 0 ? "" : "Invert each upward dependency (depend on an interface owned by the lower layer). See the Layering page.",
                "layers.html")
            : new Row("Layering violations", "n/a", Status.NA,
                "No layering contract is declared, so this cannot be checked.",
                "Add an archdiagram.layers.json at the source root declaring your layers top-to-bottom. See the Layering page.",
                "layers.html");
    }

    private static Row BuildTestRatioRow(ProjectModel model)
    {
        var firstParty = CodebaseStats.FirstPartyLoc(model);
        var testLoc = CodebaseStats.TestLoc(model);
        var ratio = firstParty + testLoc == 0 ? 0.0 : (double)testLoc / (firstParty + testLoc);
        return new Row("Test-code ratio", ratio.ToString("P0", CultureInfo.InvariantCulture),
            ratio >= 0.25 ? Status.Ok : ratio >= 0.10 ? Status.Watch : Status.Fail,
            "Test lines as a share of first-party + test lines — a rough proxy for how much is exercised, not execution coverage.",
            ratio >= 0.25 ? "" : "Add tests for the most central files first (see the Overview “Start here” list); measure real coverage with a coverage tool.",
            "index.html");
    }

    private static Row BuildDriftRow(ProjectModel model)
    {
        var driftCount = model.Projects
            .SelectMany(p => p.Packages)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Count(g => g.Select(x => x.Version).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2);
        return new Row("Package version drift", driftCount.ToString("N0"),
            driftCount == 0 ? Status.Ok : Status.Watch,
            "Packages referenced at more than one version across projects — a source of duplicate assemblies and conflict bugs.",
            driftCount == 0 ? "" : "Align the flagged packages on one version, or adopt Central Package Management. See the Packages page.",
            "packages.html");
    }

    private static Row BuildSecretsRow(ProjectModel model)
    {
        var secrets = model.Projects.SelectMany(p => p.ConnectionStrings).Count(u => u.HasCredential);
        return new Row("Credentials in source", secrets.ToString("N0"),
            secrets == 0 ? Status.Ok : Status.Fail,
            "Connection strings that embed a username/password committed to source control.",
            secrets == 0 ? "" : "Move these into user-secrets/environment variables/Key Vault and rotate any committed credential. See Config & Secrets.",
            "config.html");
    }

    private static Row BuildTodosRow(ProjectModel model)
    {
        var todos = model.Files.Where(CodebaseStats.IsFirstParty).Sum(f => f.Todos.Count);
        return new Row("TODO / FIXME markers", todos.ToString("N0"),
            todos <= 10 ? Status.Ok : todos <= 50 ? Status.Watch : Status.Fail,
            "Open markers left in first-party comments.",
            todos <= 10 ? "" : "Triage the markers into tracked issues or resolve them. See the Hotspots page.",
            "hotspots.html");
    }
}
