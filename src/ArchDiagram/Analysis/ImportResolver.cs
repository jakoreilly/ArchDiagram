using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Maps raw import strings back onto files in the scanned set.
/// Unresolved imports become external-package edges (capped later for display).</summary>
public static class ImportResolver
{
    public static List<DepEdge> Resolve(IReadOnlyList<FileNode> files)
    {
        var bySlugPath = files.ToDictionary(f => f.RelPath, f => f, StringComparer.OrdinalIgnoreCase);

        // C#: namespace -> files declaring types in it (from Roslyn facts).
        var byNamespace = new Dictionary<string, List<FileNode>>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            foreach (var ns in f.Types.Select(t => t.Namespace).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal))
            {
                (byNamespace.TryGetValue(ns, out var list) ? list : byNamespace[ns] = []).Add(f);
            }
        }

        var edges = new List<DepEdge>();
        foreach (var file in files)
        {
            foreach (var import in file.Imports)
            {
                var targets = file.Language switch
                {
                    "C#" => ResolveCSharp(import, byNamespace),
                    "TypeScript/JavaScript" => ResolveRelative(file, import, bySlugPath, [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", "/index.ts", "/index.js"]),
                    "Python" => ResolvePython(file, import, bySlugPath),
                    "PowerShell" => ResolveRelative(file, import, bySlugPath, [".ps1", ".psm1"]),
                    _ => [],
                };

                if (targets.Count == 0)
                {
                    edges.Add(new DepEdge { FromSlug = file.Slug, ExternalTarget = ExternalName(file.Language, import) });
                }
                else
                {
                    foreach (var t in targets.Where(t => t.Slug != file.Slug))
                    {
                        edges.Add(new DepEdge { FromSlug = file.Slug, ToSlug = t.Slug });
                    }
                }
            }
        }

        return edges
            .DistinctBy(e => (e.FromSlug, e.ToSlug, e.ExternalTarget))
            .OrderBy(e => e.FromSlug, StringComparer.Ordinal)
            .ThenBy(e => e.ToSlug, StringComparer.Ordinal)
            .ThenBy(e => e.ExternalTarget, StringComparer.Ordinal)
            .ToList();
    }

    private static List<FileNode> ResolveCSharp(string ns, Dictionary<string, List<FileNode>> byNamespace) =>
        byNamespace.TryGetValue(ns, out var hits) ? hits : [];

    private static List<FileNode> ResolveRelative(FileNode from, string import, Dictionary<string, FileNode> byPath, string[] extCandidates)
    {
        if (!import.StartsWith('.') && !import.Contains('/') && !import.Contains('\\')) { return []; }

        var fromDir = Path.GetDirectoryName(from.RelPath)?.Replace('\\', '/') ?? "";
        var raw = import.Replace('\\', '/').TrimStart();
        var combined = NormalizePath(fromDir.Length == 0 ? raw : fromDir + "/" + raw);

        var candidates = new List<string> { combined };
        if (!Path.HasExtension(combined) || extCandidates.All(e => !combined.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
        {
            candidates.AddRange(extCandidates.Select(e => combined + e));
        }

        foreach (var c in candidates)
        {
            if (byPath.TryGetValue(c, out var hit)) { return [hit]; }
        }
        return [];
    }

    private static List<FileNode> ResolvePython(FileNode from, string module, Dictionary<string, FileNode> byPath)
    {
        var path = module.Replace('.', '/');
        var fromDir = Path.GetDirectoryName(from.RelPath)?.Replace('\\', '/') ?? "";
        // Try repo-root-relative first, then relative to the importing script's folder.
        foreach (var basePath in new[] { path, fromDir.Length == 0 ? path : fromDir + "/" + path })
        {
            foreach (var c in new[] { basePath + ".py", basePath + "/__init__.py" })
            {
                if (byPath.TryGetValue(c, out var hit)) { return [hit]; }
            }
        }
        return [];
    }

    private static string ExternalName(string language, string import) => language switch
    {
        // Keep only the package root for npm-style imports ("@scope/pkg/deep" -> "@scope/pkg").
        "TypeScript/JavaScript" when !import.StartsWith('.') =>
            import.StartsWith('@') ? string.Join('/', import.Split('/').Take(2)) : import.Split('/')[0],
        "Python" => import.Split('.')[0],
        _ => import,
    };

    private static string NormalizePath(string path)
    {
        var parts = new List<string>();
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") { continue; }
            if (seg == ".." && parts.Count > 0) { parts.RemoveAt(parts.Count - 1); }
            else if (seg != "..") { parts.Add(seg); }
        }
        return string.Join('/', parts);
    }
}
