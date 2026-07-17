using ArchDiagram.Graph;

namespace ArchDiagram.Site;

/// <summary>Aggregates every value pages used to recompute independently from the model,
/// built exactly once per site generation. Field values equal the old per-page computations
/// exactly (same iteration order over model.FileDependencies / model.Calls / model.Files), so
/// swapping a page over to <see cref="SiteContext"/> is a pure performance change — no output
/// changes. See plan.md Phase 1.</summary>
public sealed class SiteContext
{
    public required ProjectModel Model { get; init; }
    public required IReadOnlyDictionary<string, FileNode> BySlug { get; init; }
    public required IReadOnlyDictionary<string, int> FanIn { get; init; }
    public required IReadOnlyDictionary<string, int> FanOut { get; init; }
    public required IReadOnlyDictionary<string, int> ExternalCounts { get; init; }

    /// <summary>Per-slug incoming dependency edges (always resolved — ToSlug is a real slug),
    /// in the same order as model.FileDependencies — matches the old per-file
    /// <c>.Where(e =&gt; e.ToSlug == file.Slug)</c> filter byte-for-byte.</summary>
    public required IReadOnlyDictionary<string, List<DepEdge>> IncomingDeps { get; init; }

    /// <summary>Per-slug outgoing dependency edges — ALL of them, resolved or external (an
    /// external edge has ToSlug="" and a non-empty ExternalTarget). Matches the old per-file
    /// <c>.Where(e =&gt; e.FromSlug == file.Slug)</c> filter, which carried no ToSlug condition.</summary>
    public required IReadOnlyDictionary<string, List<DepEdge>> OutgoingDeps { get; init; }

    /// <summary>Per-slug cross-file call edges (self-calls excluded), in model.Calls order.</summary>
    public required IReadOnlyDictionary<string, List<CallEdge>> CallsIn { get; init; }
    public required IReadOnlyDictionary<string, List<CallEdge>> CallsOut { get; init; }

    /// <summary>Cached analyzer results that were previously computed twice per run
    /// (once for the HTML page, once for the Markdown/Wiki export).</summary>
    public required Analysis.ScorecardBuilder.Card Scorecard { get; init; }
    public required Analysis.ArchitectureMetrics.Result Metrics { get; init; }

    /// <summary>Top 20 most-central files (see <see cref="Analysis.ImportanceScorer"/>); every
    /// caller wants fewer (12 on the Overview, 10 in the Markdown export) so they just
    /// <c>.Take(n)</c> from this cached, deterministically-ordered list.</summary>
    public required IReadOnlyList<Analysis.ImportanceScorer.Scored> Ranked { get; init; }

    /// <summary>The 3D graph payload, serialized once. Was previously built independently for
    /// graph.html, the compact Overview embed, and graph.json (3 serializations of the same data).</summary>
    public required string GraphJson { get; init; }

    public static SiteContext Build(ProjectModel model)
    {
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);

        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        var external = new Dictionary<string, int>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<DepEdge>>(StringComparer.Ordinal);
        var outgoing = new Dictionary<string, List<DepEdge>>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies)
        {
            // OutgoingDeps groups every edge FROM this file regardless of resolution — the
            // original FilePage filter (`e.FromSlug == file.Slug`) carried no ToSlug condition.
            (outgoing.TryGetValue(e.FromSlug, out var o) ? o : outgoing[e.FromSlug] = []).Add(e);

            if (e.ToSlug.Length > 0)
            {
                fanIn[e.ToSlug] = fanIn.GetValueOrDefault(e.ToSlug) + 1;
                fanOut[e.FromSlug] = fanOut.GetValueOrDefault(e.FromSlug) + 1;
                (incoming.TryGetValue(e.ToSlug, out var i) ? i : incoming[e.ToSlug] = []).Add(e);
            }
            else if (e.ExternalTarget.Length > 0)
            {
                external[e.ExternalTarget] = external.GetValueOrDefault(e.ExternalTarget) + 1;
            }
        }

        var callsIn = new Dictionary<string, List<CallEdge>>(StringComparer.Ordinal);
        var callsOut = new Dictionary<string, List<CallEdge>>(StringComparer.Ordinal);
        foreach (var c in model.Calls)
        {
            if (c.CalleeSlug != c.CallerSlug)
            {
                (callsOut.TryGetValue(c.CallerSlug, out var o) ? o : callsOut[c.CallerSlug] = []).Add(c);
                (callsIn.TryGetValue(c.CalleeSlug, out var i) ? i : callsIn[c.CalleeSlug] = []).Add(c);
            }
        }

        return new SiteContext
        {
            Model = model,
            BySlug = bySlug,
            FanIn = fanIn,
            FanOut = fanOut,
            ExternalCounts = external,
            IncomingDeps = incoming,
            OutgoingDeps = outgoing,
            CallsIn = callsIn,
            CallsOut = callsOut,
            Scorecard = Analysis.ScorecardBuilder.Build(model),
            Metrics = Analysis.ArchitectureMetrics.Compute(model),
            Ranked = Analysis.ImportanceScorer.Rank(model, 20),
            GraphJson = GraphDataWriter.BuildJson(model),
        };
    }
}
