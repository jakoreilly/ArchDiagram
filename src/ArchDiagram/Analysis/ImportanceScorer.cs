using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Ranks files by how central they are to understanding the codebase, so a
/// newcomer knows what to read first. Pure and deterministic: score is a weighted blend of
/// fan-in (who depends on me — dominant), fan-out, cross-file call centrality, an
/// entry-point bonus, and a gentle size term. No I/O.</summary>
public static class ImportanceScorer
{
    public sealed record Scored(FileNode File, double Score, int FanIn, int FanOut,
        int CallsIn, bool EntryPoint, string Reason);

    public static IReadOnlyList<Scored> Rank(ProjectModel model, int take = 12)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies)
        {
            if (e.ToSlug.Length == 0) { continue; }
            fanIn[e.ToSlug] = fanIn.GetValueOrDefault(e.ToSlug) + 1;
            fanOut[e.FromSlug] = fanOut.GetValueOrDefault(e.FromSlug) + 1;
        }
        var callsIn = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in model.Calls)
        {
            if (c.CalleeSlug != c.CallerSlug) { callsIn[c.CalleeSlug] = callsIn.GetValueOrDefault(c.CalleeSlug) + 1; }
        }

        var maxLoc = model.Files.Count == 0 ? 1 : Math.Max(1, model.Files.Max(f => f.Loc));

        var scored = new List<Scored>(model.Files.Count);
        foreach (var f in model.Files)
        {
            if (f.IsTest) { continue; }   // tests are excluded from the "read me first" ranking
            var fi = fanIn.GetValueOrDefault(f.Slug);
            var fo = fanOut.GetValueOrDefault(f.Slug);
            var ci = callsIn.GetValueOrDefault(f.Slug);
            var entry = IsEntryPoint(f);
            // Weights: fan-in is the strongest "read me first" signal.
            var score = (3.0 * fi) + (1.0 * fo) + (1.5 * ci) + (entry ? 6.0 : 0.0) + (1.0 * f.Loc / maxLoc);
            if (score <= 0) { continue; }
            scored.Add(new Scored(f, score, fi, fo, ci, entry, Reason(fi, fo, ci, entry)));
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.FanIn)
            .ThenBy(s => s.File.RelPath, StringComparer.OrdinalIgnoreCase)   // stable tiebreak
            .Take(take)
            .ToList();
    }

    private static bool IsEntryPoint(FileNode f)
    {
        var name = f.RelPath.Split('/')[^1];
        if (name.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (name.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (name.Equals("Main.cs", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (name.Equals("index.ts", StringComparison.OrdinalIgnoreCase) || name.Equals("index.js", StringComparison.OrdinalIgnoreCase)) { return true; }
        if (name.Equals("__main__.py", StringComparison.OrdinalIgnoreCase)) { return true; }
        // A declared Main method (name-only heuristic — no modifier data on MethodInfo) or the
        // synthesized top-level statements member both mark an executable entry point.
        return f.Types.Any(t => t.Kind == "top-level" || t.Methods.Any(m => m.Name == "Main"));
    }

    private static string Reason(int fanIn, int fanOut, int callsIn, bool entry)
    {
        if (entry) { return "Application entry point"; }
        if (fanIn >= fanOut && fanIn > 0) { return $"Depended on by {fanIn} file(s)"; }
        if (callsIn > 0) { return $"Called from {callsIn} site(s) elsewhere"; }
        if (fanOut > 0) { return $"Coordinates {fanOut} other file(s)"; }
        return "Central to the codebase";
    }
}
