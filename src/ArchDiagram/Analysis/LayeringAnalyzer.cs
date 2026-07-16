using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Layer analysis over the module graph. Two modes: (1) <b>declared</b> — when a layers
/// sidecar assigns modules to named, ordered layers, every cross-module edge is checked and any
/// that points from a lower layer up to a higher one is a violation of the contract; (2)
/// <b>inferred</b> — with no sidecar, modules are bucketed into levels by longest dependency path
/// (level 0 = foundational, depended-on by others) so the reviewer still sees the shape. Pure and
/// deterministic.</summary>
public static class LayeringAnalyzer
{
    public sealed record Layer(string Name, IReadOnlyList<string> Modules);
    public sealed record Violation(string FromModule, string FromLayer, string ToModule, string ToLayer);
    public sealed record Result(
        bool Declared,
        IReadOnlyList<Layer> Layers,
        IReadOnlyList<Violation> Violations,
        IReadOnlyList<string> Unassigned);

    public static Result Analyze(ProjectModel model)
    {
        var g = ModuleGrouper.Build(model);
        var moduleKeys = g.Modules.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        return model.Layers.Count > 0
            ? Declared(model, g, moduleKeys)
            : Inferred(model, g, moduleKeys);
    }

    private static Result Declared(ProjectModel model, ModuleGrouper.ModuleGraph g, List<string> moduleKeys)
    {
        // index 0 = top layer (may depend on lower). Assign each module to the first layer whose
        // prefix matches (longest prefix wins so specific layers beat general ones).
        var order = model.Layers;
        int LayerIndexOf(string moduleKey)
        {
            var best = -1; var bestLen = -1;
            for (var i = 0; i < order.Count; i++)
            {
                foreach (var ns in order[i].Namespaces)
                {
                    // Match on a namespace boundary so "App.Web" does not also claim
                    // "App.WebHost" — exact match or a dotted-segment prefix only.
                    var matches = moduleKey == ns || moduleKey.StartsWith(ns + ".", StringComparison.Ordinal);
                    if (matches && ns.Length > bestLen) { best = i; bestLen = ns.Length; }
                }
            }
            return best;
        }

        var idx = moduleKeys.ToDictionary(k => k, LayerIndexOf, StringComparer.Ordinal);
        var unassigned = moduleKeys.Where(k => idx[k] < 0).ToList();

        var layers = order.Select((l, i) => new Layer(l.Name,
            moduleKeys.Where(k => idx[k] == i).ToList())).ToList();

        var violations = new List<Violation>();
        foreach (var (from, to) in g.Edges.Keys.OrderBy(k => k.From, StringComparer.Ordinal).ThenBy(k => k.To, StringComparer.Ordinal))
        {
            if (!idx.TryGetValue(from, out var fi) || !idx.TryGetValue(to, out var ti) || fi < 0 || ti < 0) { continue; }
            // Allowed: depend downward (fi < ti) or within the same layer (fi == ti).
            // Violation: a lower layer (larger index) depends on a higher one (smaller index).
            if (fi > ti)
            {
                violations.Add(new Violation(from, order[fi].Name, to, order[ti].Name));
            }
        }
        return new Result(true, layers, violations, unassigned);
    }

    // Stability tiers (top → bottom). Instability I = Ce/(Ca+Ce): high = unstable/orchestration,
    // low = stable/foundational. Fixed bands give a small, meaningful, count-stable set of tiers
    // (never the "45 levels" that longest-path depth produced on large graphs).
    private static readonly (string Name, double Min)[] Tiers =
    [
        ("Orchestration (unstable)", 0.80),
        ("Application", 0.50),
        ("Core", 0.20),
        ("Foundational (stable)", 0.00),
    ];

    private static Result Inferred(ProjectModel model, ModuleGrouper.ModuleGraph g, List<string> moduleKeys)
    {
        // Per-module instability from the same computation the Metrics page uses.
        var inst = ArchitectureMetrics.Compute(model).Modules
            .ToDictionary(m => m.Key, m => m.Instability, StringComparer.Ordinal);

        string TierOf(string key)
        {
            var i = inst.GetValueOrDefault(key, 0.0);
            foreach (var (name, min) in Tiers) { if (i >= min) { return name; } }
            return Tiers[^1].Name;
        }

        var layers = Tiers
            .Select(t => new Layer(t.Name, moduleKeys.Where(k => TierOf(k) == t.Name)
                .OrderBy(k => k, StringComparer.Ordinal).ToList()))
            .Where(l => l.Modules.Count > 0)
            .ToList();

        // Stable Dependencies Principle: dependencies should point toward MORE stable modules
        // (higher instability depends on lower). An edge whose source is more stable than its
        // target (I(from) < I(to) by a margin) points "against the grain" — an inversion candidate.
        const double margin = 0.15;
        var violations = new List<Violation>();
        foreach (var (from, to) in g.Edges.Keys.OrderBy(k => k.From, StringComparer.Ordinal).ThenBy(k => k.To, StringComparer.Ordinal))
        {
            if (from == to) { continue; }
            var fi = inst.GetValueOrDefault(from, 0.0);
            var ti = inst.GetValueOrDefault(to, 0.0);
            if (fi < ti - margin) { violations.Add(new Violation(from, TierOf(from), to, TierOf(to))); }
        }
        return new Result(false, layers, violations, []);
    }
}
