using ArchDiagram.Analysis;
using ArchDiagram.Graph;

namespace ArchDiagram.Diff;

/// <summary>Compares two ProjectModel snapshots (typically two model.json archives from
/// different points in time) and reports what changed. Diffs files by RelPath — never by
/// Slug, which can gain a "_2" collision suffix independently in each generation and would
/// make an unrelated file look renamed. Pure and deterministic.</summary>
public static class ModelDiff
{
    public sealed record FileChange(string RelPath, int OldLoc, int NewLoc);
    public sealed record ScorecardChange(string Metric, string OldValue, string NewValue,
        ScorecardBuilder.Status OldStatus, ScorecardBuilder.Status NewStatus);

    public sealed record Result(
        string OldRootName, string NewRootName,
        List<string> AddedFiles, List<string> RemovedFiles, List<FileChange> ChangedFiles,
        List<string> AddedDependencyEdges, List<string> RemovedDependencyEdges,
        List<ScorecardChange> ScorecardChanges,
        int OldFileCount, int NewFileCount);

    public static Result Compute(ProjectModel oldModel, ProjectModel newModel)
    {
        var oldByPath = oldModel.Files.ToDictionary(f => f.RelPath, StringComparer.OrdinalIgnoreCase);
        var newByPath = newModel.Files.ToDictionary(f => f.RelPath, StringComparer.OrdinalIgnoreCase);

        var added = newByPath.Keys.Where(p => !oldByPath.ContainsKey(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = oldByPath.Keys.Where(p => !newByPath.ContainsKey(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var changed = oldByPath.Keys.Where(newByPath.ContainsKey)
            .Where(p => oldByPath[p].Loc != newByPath[p].Loc)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Select(p => new FileChange(p, oldByPath[p].Loc, newByPath[p].Loc))
            .ToList();

        // Dependency edges compared by RelPath (translated from each model's own slugs) so
        // two independently-generated models with different slug assignments still diff
        // correctly on the underlying file identity.
        var oldBySlug = oldModel.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);
        var newBySlug = newModel.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);
        var oldEdges = new HashSet<string>(oldModel.FileDependencies.Select(e => EdgeKey(e, oldBySlug)), StringComparer.Ordinal);
        var newEdges = new HashSet<string>(newModel.FileDependencies.Select(e => EdgeKey(e, newBySlug)), StringComparer.Ordinal);
        var addedEdges = newEdges.Except(oldEdges).OrderBy(e => e, StringComparer.Ordinal).ToList();
        var removedEdges = oldEdges.Except(newEdges).OrderBy(e => e, StringComparer.Ordinal).ToList();

        var oldCard = ScorecardBuilder.Build(oldModel);
        var newCard = ScorecardBuilder.Build(newModel);
        var newByMetric = newCard.Rows.ToDictionary(r => r.Metric, StringComparer.Ordinal);
        var scorecardChanges = oldCard.Rows
            .Where(r => newByMetric.ContainsKey(r.Metric))
            .Select(r => (Old: r, New: newByMetric[r.Metric]))
            .Where(x => x.Old.Value != x.New.Value || x.Old.Status != x.New.Status)
            .Select(x => new ScorecardChange(x.Old.Metric, x.Old.Value, x.New.Value, x.Old.Status, x.New.Status))
            .ToList();

        return new Result(
            oldModel.RootName, newModel.RootName,
            added, removed, changed,
            addedEdges, removedEdges,
            scorecardChanges,
            oldModel.Files.Count, newModel.Files.Count);
    }

    private static string EdgeKey(DepEdge e, Dictionary<string, FileNode> bySlug)
    {
        var from = bySlug.TryGetValue(e.FromSlug, out var ff) ? ff.RelPath : e.FromSlug;
        var to = e.ToSlug.Length > 0
            ? (bySlug.TryGetValue(e.ToSlug, out var tf) ? tf.RelPath : e.ToSlug)
            : e.ExternalTarget;
        return $"{from} -> {to}";
    }
}
