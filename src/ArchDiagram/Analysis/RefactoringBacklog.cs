using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Aggregates findings from every analysis into one prioritised, actionable backlog: what
/// to fix, why it matters, and a concrete refactoring tip, each linking to the page with the detail.
/// This is the "what do I actually do?" list for someone inheriting a brownfield codebase. Pure and
/// deterministic; ordered by severity then category.</summary>
public static class RefactoringBacklog
{
    public enum Sev { Critical, High, Medium, Low }

    // SonarSource's default cognitive-complexity gate (mirrors Site.Severity.SonarGate; kept as a
    // local constant so the Analysis layer doesn't depend on the Site layer).
    private const int CognitiveGate = 15;

    public sealed record Item(Sev Severity, string Category, string Title, string Why, string Tip, string Link);

    public static IReadOnlyList<Item> Build(ProjectModel model)
    {
        var items = new List<Item>();
        var m = ArchitectureMetrics.Compute(model);

        // Security: credentials committed to source.
        var secrets = model.Projects.SelectMany(p => p.ConnectionStrings).Count(u => u.HasCredential);
        if (secrets > 0)
        {
            items.Add(new Item(Sev.Critical, "Security", $"{secrets} connection string(s) embed credentials in source",
                "Secrets committed to source control can leak and are hard to rotate.",
                "Move them to user-secrets / environment variables / a secret store, and rotate anything already committed.", "config.html"));
        }

        // Dependency cycles.
        foreach (var cycle in m.Cycles)
        {
            items.Add(new Item(Sev.High, "Cycle", "Dependency cycle: " + string.Join(" → ", cycle) + " → …",
                "Cyclic modules can't be built, tested or understood in isolation.",
                "Break the loop: introduce an interface owned by one module and depend on it, or move the shared type down a layer.", "metrics.html"));
        }

        // Layering / dependency-direction violations.
        var layering = LayeringAnalyzer.Analyze(model);
        foreach (var v in layering.Violations.Take(10))
        {
            items.Add(layering.Declared
                ? new Item(Sev.High, "Layering", $"Upward dependency: {v.FromModule} → {v.ToModule}",
                    $"{v.FromModule} ({v.FromLayer}) depends on {v.ToModule} ({v.ToLayer}), breaking the declared layer contract.",
                    "Invert it: depend on an interface owned by the lower layer.", "layers.html")
                : new Item(Sev.Medium, "Dependency direction", $"Against-the-grain: {v.FromModule} → {v.ToModule}",
                    $"A more-stable module ({v.FromModule}) depends on a less-stable one ({v.ToModule}) — the Stable Dependencies Principle in reverse.",
                    "Have both depend on an abstraction owned by the stable side.", "layers.html"));
        }

        // Zone-of-pain modules.
        foreach (var mod in m.Modules.Where(x => ArchitectureMetrics.Classify(x.Instability, x.Abstractness, x.Ca, x.IsPureData) == ArchitectureMetrics.Zone.ZoneOfPain).Take(8))
        {
            items.Add(new Item(Sev.Medium, "Rigidity", $"Zone of pain: {mod.Key}",
                $"{mod.Key} is concrete and heavily depended-on (Ca={mod.Ca}), so changes ripple and there are no contracts to depend on instead.",
                "Extract interfaces for its public surface so dependents rely on contracts; or accept it if it's a stable leaf.", "metrics.html"));
        }

        // Poor-maintainability files.
        foreach (var s in MaintainabilityScorer.Rank(model).Where(s => s.Band == MaintainabilityScorer.Band.Poor).Take(10))
        {
            items.Add(new Item(s.Score < 25 ? Sev.High : Sev.Medium, "Maintainability",
                $"Hard to maintain: {s.File.RelPath} (score {s.Score})",
                $"{s.Loc:N0} LOC, peak cognitive {s.MaxCognitive}, coupling {s.Coupling} — risky to change safely.",
                "Add characterization tests first, then split cohesive responsibilities into smaller types.", $"files/{s.File.Slug}.html"));
        }

        // High-complexity methods.
        var complex = model.Files.Where(CodebaseStats.IsFirstParty)
            .SelectMany(f => f.Types.SelectMany(t => t.Methods.Select(me => (f, me))))
            .Where(x => x.me.Cognitive >= CognitiveGate)
            .OrderByDescending(x => x.me.Cognitive).Take(8).ToList();
        foreach (var (f, me) in complex)
        {
            items.Add(new Item(Sev.Medium, "Complexity", $"Complex method: {me.Name} in {f.RelPath.Split('/')[^1]} (cognitive {me.Cognitive})",
                "High cognitive complexity is hard to read and test.",
                "Extract guard clauses and nested blocks into well-named helper methods to flatten the control flow.", $"files/{f.Slug}.html"));
        }

        // Version drift.
        var drift = model.Projects.SelectMany(p => p.Packages)
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Version).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .ToList();
        foreach (var g in drift.Take(10))
        {
            items.Add(new Item(Sev.Medium, "Dependencies", $"Version drift: {g.Key}",
                "Referenced at more than one version across projects — risk of duplicate assemblies and conflicts.",
                "Align on a single version, or adopt Central Package Management (Directory.Packages.props).", "packages.html"));
        }

        // Dead code candidates (orphans). Same-namespace/qualified-name references need no
        // `using`, so the import graph alone under-counts connectivity — the heuristic call
        // graph (name+arity matched, namespace-independent) catches what imports miss.
        if (model.FileDependencies.Any(e => e.ToSlug.Length > 0) || model.Calls.Count > 0)
        {
            var connected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in model.FileDependencies.Where(e => e.ToSlug.Length > 0)) { connected.Add(e.FromSlug); connected.Add(e.ToSlug); }
            foreach (var c in model.Calls) { connected.Add(c.CallerSlug); connected.Add(c.CalleeSlug); }
            var orphans = model.Files.Count(f => CodebaseStats.IsFirstParty(f) && !connected.Contains(f.Slug) && f.Loc > 0);
            if (orphans > 0)
            {
                items.Add(new Item(Sev.Low, "Dead code", $"{orphans} unreferenced file(s)",
                    "Files with no incoming or outgoing internal links — possible dead code (or standalone entry points).",
                    "Confirm each is truly unused (reflection, DI, config-driven?) before deleting.", "hotspots.html"));
            }
        }

        // Open markers.
        var todos = model.Files.Where(CodebaseStats.IsFirstParty).Sum(f => f.Todos.Count);
        if (todos > 10)
        {
            items.Add(new Item(Sev.Low, "Hygiene", $"{todos} TODO/FIXME markers",
                "A backlog of inline markers tends to rot and hide real work.",
                "Triage into tracked issues or resolve; keep inline markers short-lived.", "hotspots.html"));
        }

        return items
            .OrderBy(i => (int)i.Severity).ThenBy(i => i.Category, StringComparer.Ordinal).ThenBy(i => i.Title, StringComparer.Ordinal)
            .ToList();
    }
}
