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
            : Inferred(g, moduleKeys);
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
                    if (moduleKey.StartsWith(ns, StringComparison.Ordinal) && ns.Length > bestLen)
                    {
                        best = i; bestLen = ns.Length;
                    }
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

    private static Result Inferred(ModuleGrouper.ModuleGraph g, List<string> moduleKeys)
    {
        // Longest-path level: level(A) = 1 + max(level of modules A depends on). Relax up to
        // n times; cycles saturate at the cap (still grouped, just not perfectly layered).
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in g.Edges.Keys)
        {
            if (from == to) { continue; }
            (deps.TryGetValue(from, out var l) ? l : deps[from] = []).Add(to);
        }
        var level = moduleKeys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var cap = moduleKeys.Count;
        for (var iter = 0; iter < cap; iter++)
        {
            var changed = false;
            foreach (var k in moduleKeys)
            {
                if (!deps.TryGetValue(k, out var ds)) { continue; }
                var want = ds.Where(level.ContainsKey).Select(d => level[d] + 1).DefaultIfEmpty(0).Max();
                if (want > level[k] && want <= cap) { level[k] = want; changed = true; }
            }
            if (!changed) { break; }
        }

        var maxLevel = level.Count > 0 ? level.Values.Max() : 0;
        var layers = new List<Layer>();
        // Present top (highest level = orchestration) to bottom (level 0 = foundational).
        for (var lv = maxLevel; lv >= 0; lv--)
        {
            var members = moduleKeys.Where(k => level[k] == lv).ToList();
            if (members.Count > 0) { layers.Add(new Layer($"Level {lv}", members)); }
        }
        return new Result(false, layers, [], []);
    }
}
