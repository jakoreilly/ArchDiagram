using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Finds a representative "how do you get here" path through the internal dependency
/// graph to a key file: the shortest chain from an entry point (a file nothing else imports,
/// but which imports others) to the target. Helps a reviewer see the code path that leads into
/// the files that matter. Pure and deterministic (all iteration is over sorted slugs).</summary>
public static class CriticalPaths
{
    private sealed record Graph(
        Dictionary<string, List<string>> Adj,
        IReadOnlyList<string> EntryPoints);

    /// <summary>Internal forward-dependency graph + entry points, built once. Entry points are
    /// files imported by nothing internally but that import something (real roots).</summary>
    private static Graph BuildGraph(ProjectModel model)
    {
        var slugs = model.Files.Select(f => f.Slug).ToHashSet(StringComparer.Ordinal);
        var adj = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var hasIncoming = new HashSet<string>(StringComparer.Ordinal);
        var hasOutgoing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies)
        {
            if (e.ToSlug.Length == 0 || !slugs.Contains(e.FromSlug) || !slugs.Contains(e.ToSlug)) { continue; }
            if (e.FromSlug == e.ToSlug) { continue; }
            (adj.TryGetValue(e.FromSlug, out var l) ? l : adj[e.FromSlug] = []).Add(e.ToSlug);
            hasIncoming.Add(e.ToSlug);
            hasOutgoing.Add(e.FromSlug);
        }
        foreach (var l in adj.Values) { l.Sort(StringComparer.Ordinal); }
        var entryPoints = model.Files
            .Select(f => f.Slug)
            .Where(s => !hasIncoming.Contains(s) && hasOutgoing.Contains(s))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        return new Graph(adj, entryPoints);
    }

    private static IReadOnlyList<string>? ShortestPath(Graph g, string targetSlug)
    {
        if (g.EntryPoints.Count == 0 || g.EntryPoints.Contains(targetSlug)) { return null; }
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var ep in g.EntryPoints) { seen.Add(ep); queue.Enqueue(ep); }
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == targetSlug)
            {
                var path = new List<string>();
                for (var at = cur; at is not null; at = parent.GetValueOrDefault(at)) { path.Add(at); }
                path.Reverse();
                return path;
            }
            if (!g.Adj.TryGetValue(cur, out var next)) { continue; }
            foreach (var n in next) { if (seen.Add(n)) { parent[n] = cur; queue.Enqueue(n); } }
        }
        return null;
    }

    /// <summary>Shortest entry-point → target path as a list of slugs (inclusive of both ends),
    /// or null when the target is itself an entry point or is unreachable from any entry point.</summary>
    public static IReadOnlyList<string>? ToFile(ProjectModel model, string targetSlug) =>
        ShortestPath(BuildGraph(model), targetSlug);

    public sealed record KeyPath(string TargetSlug, string TargetLabel, IReadOnlyList<string> Nodes);

    /// <summary>Critical paths to the top <paramref name="take"/> most-central first-party files:
    /// each is the shortest entry-point → file chain. Builds the graph once (unlike calling
    /// <see cref="ToFile"/> per file). Only files with a real inbound path are returned.</summary>
    public static IReadOnlyList<KeyPath> AllToKeyFiles(ProjectModel model, int take)
    {
        var g = BuildGraph(model);
        var result = new List<KeyPath>();
        foreach (var s in ImportanceScorer.Rank(model, take).Where(s => CodebaseStats.IsFirstParty(s.File)))
        {
            var path = ShortestPath(g, s.File.Slug);
            if (path is { Count: > 1 })
            {
                result.Add(new KeyPath(s.File.Slug, s.File.RelPath.Split('/')[^1], path));
            }
        }
        return result;
    }
}
