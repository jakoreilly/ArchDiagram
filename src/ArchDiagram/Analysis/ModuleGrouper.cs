using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Clusters files into modules and aggregates the import edges between whole
/// modules — the mid-level view between the per-file Dependencies page and the whole-project
/// Overview. Pure and deterministic. The key space is chosen once per model: C# namespaces
/// when most analysed files have one, otherwise the top two path segments.</summary>
public static class ModuleGrouper
{
    public sealed record Module(string Key, int FileCount, int Loc, int AbstractTypes, int TotalTypes);

    public sealed record ModuleGraph(
        IReadOnlyList<Module> Modules,
        IReadOnlyDictionary<(string From, string To), int> Edges,
        string Mode)  // "namespace" or "folder"
    {
        public int CrossModuleLinks => Edges.Values.Sum();
    }

    public static ModuleGraph Build(ProjectModel model)
    {
        // The module map is a code-architecture view — test files are excluded (the viewer's
        // 🧪 toggle governs the HTML lists; this pre-rendered diagram/matrix stays code-only).
        var codeFiles = model.Files.Where(f => !f.IsTest).ToList();

        var withTypes = codeFiles.Count(f => f.Types.Count > 0);
        var withNamespace = codeFiles.Count(f => f.Types.Any(t => t.Namespace.Length > 0));
        var useNamespace = withTypes > 0 && withNamespace >= 0.6 * withTypes;
        var mode = useNamespace ? "namespace" : "folder";

        // In namespace mode, restrict to files that actually declare types. Otherwise docs, config,
        // CI, scripts and other type-less files all collapse into one ambiguous "(no namespace)"
        // pseudo-module that skews coupling/Instability/Distance. Folder mode keeps every file
        // (a folder is a meaningful grouping for any file type).
        if (useNamespace) { codeFiles = codeFiles.Where(f => f.Types.Count > 0).ToList(); }

        var keyOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new Dictionary<string, (int Count, int Loc, int Abstract, int Total)>(StringComparer.Ordinal);
        foreach (var f in codeFiles)
        {
            var key = useNamespace ? NamespaceKey(f) : FolderKey(f);
            keyOf[f.Slug] = key;
            var abstractTypes = f.Types.Count(t => t.Kind == "interface" || t.Modifiers.Contains("abstract"));
            var cur = files.GetValueOrDefault(key);
            files[key] = (cur.Count + 1, cur.Loc + f.Loc, cur.Abstract + abstractTypes, cur.Total + f.Types.Count);
        }

        var edges = new Dictionary<(string, string), int>();
        foreach (var e in model.FileDependencies)
        {
            if (e.ToSlug.Length == 0) { continue; }
            // Skip edges touching an excluded (test) file — neither endpoint is a module here.
            if (!keyOf.TryGetValue(e.FromSlug, out var from) || !keyOf.TryGetValue(e.ToSlug, out var to)) { continue; }
            if (string.Equals(from, to, StringComparison.Ordinal)) { continue; }
            edges[(from, to)] = edges.GetValueOrDefault((from, to)) + 1;
        }

        var modules = files
            .Select(kv => new Module(kv.Key, kv.Value.Count, kv.Value.Loc, kv.Value.Abstract, kv.Value.Total))
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToList();

        return new ModuleGraph(modules, edges, mode);
    }

    private static string NamespaceKey(FileNode f)
    {
        // Global-namespace code (e.g. a top-level-statements Program.cs) has types but no
        // namespace. Rather than pool it into an ambiguous "(no namespace)" module, give it a
        // folder-derived key so it joins a real area of the tree.
        var ns = f.Types.Select(t => t.Namespace).FirstOrDefault(n => n.Length > 0);
        return ns ?? FolderKey(f);
    }

    private static string FolderKey(FileNode f)
    {
        var parts = f.RelPath.Split('/');
        if (parts.Length <= 1) { return "(root)"; }
        return parts.Length >= 3 ? $"{parts[0]}/{parts[1]}" : parts[0];
    }
}
