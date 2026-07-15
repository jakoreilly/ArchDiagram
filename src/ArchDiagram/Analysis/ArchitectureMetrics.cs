using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Quantitative, module-level architecture metrics computed from the same module
/// clustering as the Modules page (so the two never disagree). Pure and deterministic.
///
/// Per module: afferent/efferent coupling (Ca/Ce), Robert C. Martin's Instability
/// I = Ce/(Ca+Ce), Abstractness A = abstract types / total types, and Distance from the main
/// sequence D = |A + I − 1|. Whole graph: propagation cost (transitive-closure density) and
/// dependency cycles (strongly-connected module groups). All numbers are heuristic — the analysis
/// is syntax-only and namespace import edges are capped upstream — so read them as strong hints.</summary>
public static class ArchitectureMetrics
{
    /// <summary>Above this module count the O(n²) closure is skipped (propagation cost = 0,
    /// no cycles reported) to stay cheap; the page notes it. Real module counts are far below.</summary>
    private const int MaxClosureModules = 400;

    public sealed record ModuleMetric(string Key, int Files, int Loc, int Ca, int Ce,
        double Instability, double Abstractness, double Distance);

    public enum Zone { Healthy, ZoneOfPain, ZoneOfUselessness, BenignLeaf, Watch }

    /// <summary>Plain-language zone from Instability/Abstractness. Pure. Ca disambiguates a
    /// rigid painful module (has dependents) from a harmless concrete leaf (none).</summary>
    public static Zone Classify(double instability, double abstractness, int ca)
    {
        var d = Math.Abs(abstractness + instability - 1.0);
        if (d <= 0.3) { return Zone.Healthy; }
        if (instability <= 0.3 && abstractness <= 0.3) { return ca > 0 ? Zone.ZoneOfPain : Zone.BenignLeaf; }
        if (instability >= 0.7 && abstractness >= 0.7) { return Zone.ZoneOfUselessness; }
        return Zone.Watch;
    }

    public sealed record Result(
        IReadOnlyList<ModuleMetric> Modules,
        double PropagationCost,
        IReadOnlyList<IReadOnlyList<string>> Cycles,
        string Mode,
        bool ClosureSkipped);

    public static Result Compute(ProjectModel model)
    {
        var g = ModuleGrouper.Build(model);

        // Distinct-module coupling degrees from the cross-module edges.
        var ce = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var ca = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in g.Edges.Keys)
        {
            (ce.TryGetValue(from, out var o) ? o : ce[from] = new(StringComparer.Ordinal)).Add(to);
            (ca.TryGetValue(to, out var i) ? i : ca[to] = new(StringComparer.Ordinal)).Add(from);
        }

        var metrics = g.Modules.Select(m =>
        {
            var ceN = ce.GetValueOrDefault(m.Key)?.Count ?? 0;
            var caN = ca.GetValueOrDefault(m.Key)?.Count ?? 0;
            var instability = caN + ceN == 0 ? 0.0 : (double)ceN / (caN + ceN);
            var abstractness = m.TotalTypes == 0 ? 0.0 : (double)m.AbstractTypes / m.TotalTypes;
            var distance = Math.Abs(abstractness + instability - 1.0);
            return new ModuleMetric(m.Key, m.FileCount, m.Loc, caN, ceN, instability, abstractness, distance);
        }).ToList();

        var skip = g.Modules.Count > MaxClosureModules;
        var reach = skip ? null : Reachability(g);
        var propagation = skip || g.Modules.Count == 0 ? 0.0
            : (double)reach!.Values.Sum(s => s.Count) / (g.Modules.Count * g.Modules.Count);
        var cycles = skip ? [] : Cycles(g, reach!);

        var ordered = metrics
            .OrderByDescending(m => m.Distance).ThenBy(m => m.Key, StringComparer.Ordinal)
            .ToList();

        return new Result(ordered, propagation, cycles, g.Mode, skip);
    }

    /// <summary>For each module, the set of modules it depends on transitively, including itself
    /// (the visibility set). BFS from every module in sorted order — deterministic.</summary>
    private static Dictionary<string, HashSet<string>> Reachability(ModuleGrouper.ModuleGraph g)
    {
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in g.Edges.Keys.OrderBy(k => k.From, StringComparer.Ordinal).ThenBy(k => k.To, StringComparer.Ordinal))
        {
            (adj.TryGetValue(from, out var l) ? l : adj[from] = []).Add(to);
        }

        var reach = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var m in g.Modules)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { m.Key };
            var queue = new Queue<string>();
            queue.Enqueue(m.Key);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!adj.TryGetValue(cur, out var next)) { continue; }
                foreach (var n in next) { if (seen.Add(n)) { queue.Enqueue(n); } }
            }
            reach[m.Key] = seen;
        }
        return reach;
    }

    /// <summary>Strongly-connected module groups (size ≥ 2): two modules are in a cycle iff each
    /// reaches the other. Grouped deterministically over the precomputed closure.</summary>
    private static List<IReadOnlyList<string>> Cycles(ModuleGrouper.ModuleGraph g, Dictionary<string, HashSet<string>> reach)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var groups = new List<IReadOnlyList<string>>();
        foreach (var m in g.Modules)
        {
            if (assigned.Contains(m.Key)) { continue; }
            var group = g.Modules
                .Where(o => reach[m.Key].Contains(o.Key) && reach[o.Key].Contains(m.Key))
                .Select(o => o.Key)
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();
            if (group.Count >= 2)
            {
                groups.Add(group);
                foreach (var k in group) { assigned.Add(k); }
            }
        }
        return groups.OrderBy(x => x[0], StringComparer.Ordinal).ToList();
    }
}
