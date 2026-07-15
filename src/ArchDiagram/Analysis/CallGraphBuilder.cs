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

    public static List<CallEdge> Build(IReadOnlyList<FileNode> files)
    {
        // (name, arity) -> declaring (file, type, method) across the whole scanned set.
        var declared = new Dictionary<(string Name, int Arity), List<(FileNode File, string Type, string Method)>>();
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                foreach (var method in type.Methods)
                {
                    var key = (method.Name, method.Arity);
                    (declared.TryGetValue(key, out var list) ? list : declared[key] = []).Add((file, type.Name, method.Name));
                }
            }
        }

        var edges = new List<CallEdge>();
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                foreach (var method in type.Methods)
                {
                    foreach (var inv in method.Invocations)
                    {
                        if (!declared.TryGetValue((inv.Name, inv.Arity), out var candidates)) { continue; }
                        // BCL-common names only count when a same-name+arity method is
                        // declared in the scanned set — which is exactly this lookup — but
                        // even then, suppress if it could just as well be the BCL one
                        // (candidate declared in a different type than any we can see).
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
}
