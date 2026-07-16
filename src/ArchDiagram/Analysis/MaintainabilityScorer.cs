using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>A heuristic 0–100 maintainability score per file, blending three risk drivers we can
/// measure syntactically: size (LOC), peak method complexity (cognitive), and coupling (fan-in +
/// fan-out). It is NOT the Microsoft Maintainability Index (that needs Halstead volume, which we
/// don't compute) — it's an honest, deterministic proxy for "how hard is this file to change safely".
/// Lower = riskier. Pure.</summary>
public static class MaintainabilityScorer
{
    public enum Band { Good, Moderate, Poor }

    public sealed record FileScore(FileNode File, int Score, Band Band, int Loc, int MaxCognitive, int Coupling);

    /// <summary>Blend the three penalties into 0–100 (100 = most maintainable). Caps keep any one
    /// driver from dominating; weights favour complexity, then size, then coupling.</summary>
    public static int Score(int loc, int maxCognitive, int coupling)
    {
        var penalty = Math.Min(35, loc / 20.0)          // ~700 LOC → −35
                    + Math.Min(40, maxCognitive * 2.0)  // cognitive 20 → −40
                    + Math.Min(25, coupling * 1.5);     // coupling ~17 → −25
        return (int)Math.Round(Math.Clamp(100 - penalty, 0, 100));
    }

    public static Band ToBand(int score) => score >= 70 ? Band.Good : score >= 40 ? Band.Moderate : Band.Poor;

    /// <summary>Score every first-party file, worst (lowest) first. Deterministic.</summary>
    public static IReadOnlyList<FileScore> Rank(ProjectModel model)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies.Where(e => e.ToSlug.Length > 0))
        {
            fanOut[e.FromSlug] = fanOut.GetValueOrDefault(e.FromSlug) + 1;
            fanIn[e.ToSlug] = fanIn.GetValueOrDefault(e.ToSlug) + 1;
        }

        return model.Files
            .Where(f => CodebaseStats.IsFirstParty(f) && f.Loc > 0)
            .Select(f =>
            {
                var maxCog = f.Types.SelectMany(t => t.Methods).Select(m => m.Cognitive).DefaultIfEmpty(0).Max();
                var coupling = fanIn.GetValueOrDefault(f.Slug) + fanOut.GetValueOrDefault(f.Slug);
                var score = Score(f.Loc, maxCog, coupling);
                return new FileScore(f, score, ToBand(score), f.Loc, maxCog, coupling);
            })
            .OrderBy(s => s.Score).ThenBy(s => s.File.RelPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
