using ArchDiagram.Rendering;

namespace ArchDiagram.Graph;

/// <summary>Keeps every emitted diagram readable: caps node count by keeping the
/// highest-degree nodes and collapsing the rest into one dashed aggregate node,
/// with parallel edges to/from the aggregate merged and counted.</summary>
public static class GraphReducer
{
    public const string AggregateId = "__others__";

    public static (List<DiagramNode> Nodes, List<DiagramEdge> Edges) TrimToMax(
        IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges, int maxNodes)
    {
        if (nodes.Count <= maxNodes) { return (nodes.ToList(), edges.ToList()); }

        var degree = nodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (degree.ContainsKey(e.FromId)) { degree[e.FromId]++; }
            if (degree.ContainsKey(e.ToId)) { degree[e.ToId]++; }
        }

        // Stable: degree desc, then original order.
        var keep = nodes
            .Select((n, idx) => (Node: n, Idx: idx))
            .OrderByDescending(x => degree[x.Node.Id])
            .ThenBy(x => x.Idx)
            .Take(maxNodes - 1)
            .OrderBy(x => x.Idx)
            .Select(x => x.Node)
            .ToList();
        var keptIds = new HashSet<string>(keep.Select(n => n.Id), StringComparer.Ordinal);
        var dropped = nodes.Count - keep.Count;

        var outNodes = new List<DiagramNode>(keep)
        {
            new(AggregateId, $"… and {dropped} more", "aggregate",
                Tooltip: $"{dropped} lower-connectivity nodes were collapsed to keep this diagram readable. Use the drill-down pages for full detail."),
        };

        // Redirect edges touching dropped nodes to the aggregate; merge + count parallels.
        var merged = new Dictionary<(string From, string To, bool Dashed), int>();
        var direct = new List<DiagramEdge>();
        foreach (var e in edges)
        {
            var from = keptIds.Contains(e.FromId) ? e.FromId : AggregateId;
            var to = keptIds.Contains(e.ToId) ? e.ToId : AggregateId;
            if (from == AggregateId && to == AggregateId) { continue; }
            if (from == e.FromId && to == e.ToId) { direct.Add(e); continue; }
            var key = (from, to, e.Dashed);
            merged[key] = merged.GetValueOrDefault(key) + 1;
        }

        var outEdges = new List<DiagramEdge>(direct);
        foreach (var ((from, to, dashed), count) in merged.OrderBy(kv => kv.Key.From, StringComparer.Ordinal).ThenBy(kv => kv.Key.To, StringComparer.Ordinal))
        {
            outEdges.Add(new DiagramEdge(from, to, count == 1 ? "" : $"{count} links", dashed));
        }

        return (outNodes, outEdges);
    }
}
