using System.Text;
using System.Text.Json;
using ArchDiagram.Graph;

namespace ArchDiagram.Site;

/// <summary>Writes assets/search-index.js — a plain JS assignment (not JSON fetched
/// at runtime, which fails on file://) holding every file, type and method so the
/// Ctrl+K palette can search offline. Entries: [kind, name, detail, href].</summary>
public static class SearchIndexWriter
{
    private const int MaxEntries = 30000;

    public static void Write(ProjectModel model, string path)
    {
        var entries = new List<string[]>();

        // Files first (the smoke test asserts an entry per file), then types, then methods,
        // stopping the instant the cap is hit so a huge codebase can't bloat every page load.
        bool AtCap() => entries.Count >= MaxEntries;

        foreach (var f in model.Files)
        {
            if (AtCap()) { break; }
            entries.Add(["file", f.RelPath, f.Purpose, $"files/{f.Slug}.html"]);
        }
        foreach (var f in model.Files)
        {
            if (AtCap()) { break; }
            foreach (var t in f.Types)
            {
                if (AtCap()) { break; }
                var full = t.Namespace.Length > 0 ? $"{t.Namespace}.{t.Name}" : t.Name;
                entries.Add([t.Kind, full, f.RelPath, $"files/{f.Slug}.html"]);
            }
        }
        foreach (var f in model.Files)
        {
            if (AtCap()) { break; }
            foreach (var t in f.Types)
            {
                if (AtCap()) { break; }
                foreach (var m in t.Methods)
                {
                    if (AtCap()) { break; }
                    entries.Add(["method", $"{t.Name}.{m.Name}", m.Signature, $"files/{f.Slug}.html"]);
                }
            }
        }

        var json = JsonSerializer.Serialize(entries);
        File.WriteAllText(path, "window.ARCH_SEARCH_INDEX = " + json + ";\n", new UTF8Encoding(false));
    }
}
