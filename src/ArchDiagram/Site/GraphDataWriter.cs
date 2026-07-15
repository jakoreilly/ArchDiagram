using System.Text;
using System.Text.Json;
using ArchDiagram.Graph;
using ArchDiagram.Rendering;

namespace ArchDiagram.Site;

/// <summary>Emits graph.json for the 3D viewer: file nodes with the five data
/// channels precomputed (folder, language, size, fan-in, fan-out), plus import
/// and heuristic-call edges. Deterministic ordering (model file order).</summary>
public static class GraphDataWriter
{
    private const int MaxNodes = 2000, MaxEdges = 8000;

    public static void Write(ProjectModel model, string path)
    {
        File.WriteAllText(path, BuildJson(model), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Builds the viewer payload as a JSON string. Written to graph.json for
    /// external tooling and embedded inline in graph.html so the graph works from
    /// file:// (where fetch() is blocked).</summary>
    public static string BuildJson(ProjectModel model)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies.Where(e => e.ToSlug.Length > 0))
        {
            fanOut[e.FromSlug] = fanOut.GetValueOrDefault(e.FromSlug) + 1;
            fanIn[e.ToSlug] = fanIn.GetValueOrDefault(e.ToSlug) + 1;
        }

        var nodes = model.Files.Take(MaxNodes).Select(f => new
        {
            id = f.Slug,
            label = f.RelPath.Split('/')[^1],
            path = f.RelPath,
            folder = f.RelPath.Contains('/') ? f.RelPath[..f.RelPath.IndexOf('/')] : "(root)",
            lang = f.Language,
            loc = f.Loc,
            fanIn = fanIn.GetValueOrDefault(f.Slug),
            fanOut = fanOut.GetValueOrDefault(f.Slug),
            test = IsTestFile(f.RelPath),
            href = $"files/{f.Slug}.html",
        }).ToList();

        var shown = new HashSet<string>(nodes.Select(n => n.id), StringComparer.Ordinal);
        var edges = model.FileDependencies
            .Where(e => e.ToSlug.Length > 0 && shown.Contains(e.FromSlug) && shown.Contains(e.ToSlug))
            .Select(e => new { source = e.FromSlug, target = e.ToSlug, kind = "import" })
            .Concat(model.Calls
                .Where(c => c.CallerSlug != c.CalleeSlug && shown.Contains(c.CallerSlug) && shown.Contains(c.CalleeSlug))
                .Select(c => new { source = c.CallerSlug, target = c.CalleeSlug, kind = "call" }))
            .DistinctBy(e => (e.source, e.target, e.kind))
            .Take(MaxEdges).ToList();

        var payload = new
        {
            rootName = model.RootName,
            sourceLink = model.SourceLink,
            totalFiles = model.Files.Count,
            shownNodes = nodes.Count,
            nodes,
            edges,
        };
        return JsonSerializer.Serialize(payload, ModelJsonWriter.Options);
    }

    /// <summary>Heuristic: does this file look like automated-test code? Path-based
    /// (folder named test/tests/spec, or filename ending Tests/Test/Spec, or a
    /// .test/.spec segment). Plain string ops only (no regex). Mirrors the folder
    /// heuristic in PurposeHeuristics.</summary>
    public static bool IsTestFile(string relPath)
    {
        var p = relPath.Replace('\\', '/');
        foreach (var seg in p.Split('/'))
        {
            var s = seg.ToLowerInvariant();
            if (s is "test" or "tests" or "spec" or "specs" or "__tests__") { return true; }
        }
        var name = p[(p.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name; // keep original case for boundary detection
        // PascalCase C# convention (FooTests / FooTest / FooSpec) — case-sensitive so
        // "contest" (lowercase) is not mistaken for a "…Test" suffix.
        if (stem.EndsWith("Tests") || stem.EndsWith("Test") || stem.EndsWith("Spec")) { return true; }
        // Lower-case JS/TS/Go/Python conventions with an explicit separator boundary.
        var s2 = stem.ToLowerInvariant();
        return s2.Contains(".test") || s2.Contains(".spec")
            || s2.Contains("_test") || s2.Contains("_spec")
            || s2.StartsWith("test_") || s2.StartsWith("spec_");
    }
}
