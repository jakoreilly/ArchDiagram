using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Heuristic cross-file call graph: an invocation matches a declared method
/// when name AND arity match. No semantic model exists (syntax-only parsing), so:
/// multi-candidate matches are flagged Ambiguous (rendered dashed), and common
/// BCL/LINQ names are suppressed to avoid linking every ToString() to every other.</summary>
public static class CallGraphBuilder
{
    private static readonly HashSet<string> BclCommonNames = new(StringComparer.Ordinal)
    {
        "ToString", "Equals", "GetHashCode", "Add", "Remove", "Contains", "Count", "Dispose",
        "GetType", "CompareTo", "Parse", "TryParse", "Clone", "Where", "Select", "First",
        "FirstOrDefault", "Any", "ToList", "ToArray", "ContainsKey", "TryGetValue",
    };

    private const int MaxCandidates = 4; // more than this and a name-only match means nothing

    private readonly record struct Decl(FileNode File, string Type, string Method, int Min, int Max);

    public static List<CallEdge> Build(IReadOnlyList<FileNode> files)
    {
        var declared = BuildDeclaredIndex(files);
        var edges = ResolveEdges(files, declared);
        return edges
            .DistinctBy(e => (e.CallerSlug, e.CallerType, e.CallerMethod, e.CalleeSlug, e.CalleeType, e.CalleeMethod))
            .OrderBy(e => e.CallerSlug, StringComparer.Ordinal)
            .ThenBy(e => e.CallerType, StringComparer.Ordinal)
            .ThenBy(e => e.CallerMethod, StringComparer.Ordinal)
            .ThenBy(e => e.CalleeSlug, StringComparer.Ordinal)
            .ThenBy(e => e.CalleeType, StringComparer.Ordinal)
            .ThenBy(e => e.CalleeMethod, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>name -> declaring methods across the whole scanned set, each with its legal
    /// argument range so optional/params/named-arg calls still match.</summary>
    private static Dictionary<string, List<Decl>> BuildDeclaredIndex(IReadOnlyList<FileNode> files)
    {
        var declared = new Dictionary<string, List<Decl>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                foreach (var method in type.Methods)
                {
                    // Fall back to exact Arity when a method predates the min/max fields.
                    var min = method.MaxArity == 0 && method.MinArity == 0 ? method.Arity : method.MinArity;
                    var max = method.MaxArity == 0 && method.MinArity == 0 ? method.Arity : method.MaxArity;
                    (declared.TryGetValue(method.Name, out var list) ? list : declared[method.Name] = [])
                        .Add(new Decl(file, type.Name, method.Name, min, max));
                }
            }
        }
        return declared;
    }

    private static List<CallEdge> ResolveEdges(IReadOnlyList<FileNode> files, Dictionary<string, List<Decl>> declared)
    {
        var edges = new List<CallEdge>();
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                foreach (var method in type.Methods)
                {
                    foreach (var inv in method.Invocations)
                    {
                        if (!declared.TryGetValue(inv.Name, out var all)) { continue; }
                        var candidates = all.Where(d => inv.Arity >= d.Min && inv.Arity <= d.Max).ToList();
                        if (candidates.Count == 0) { continue; }
                        // BCL-common names only count when exactly one declaration matches.
                        if (BclCommonNames.Contains(inv.Name) && candidates.Count != 1) { continue; }
                        if (candidates.Count > MaxCandidates) { continue; }

                        var ambiguous = candidates.Count > 1;
                        foreach (var c in candidates)
                        {
                            if (c.File.Slug == file.Slug && c.Type == type.Name && c.Method == method.Name) { continue; } // self edge
                            edges.Add(new CallEdge
                            {
                                CallerSlug = file.Slug,
                                CallerType = type.Name,
                                CallerMethod = method.Name,
                                CalleeSlug = c.File.Slug,
                                CalleeType = c.Type,
                                CalleeMethod = c.Method,
                                Ambiguous = ambiguous,
                            });
                        }
                    }
                }
            }
        }
        return edges;
    }
}
