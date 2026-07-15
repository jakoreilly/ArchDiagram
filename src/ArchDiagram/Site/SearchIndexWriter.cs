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

        foreach (var f in model.Files)
        {
            entries.Add(["file", f.RelPath, f.Purpose, $"files/{f.Slug}.html"]);
        }
        foreach (var f in model.Files)
        {
            foreach (var t in f.Types)
            {
                if (entries.Count >= MaxEntries) { break; }
                var full = t.Namespace.Length > 0 ? $"{t.Namespace}.{t.Name}" : t.Name;
                entries.Add([t.Kind, full, f.RelPath, $"files/{f.Slug}.html"]);
            }
        }
        foreach (var f in model.Files)
        {
            foreach (var t in f.Types)
            {
                foreach (var m in t.Methods)
                {
                    if (entries.Count >= MaxEntries) { break; }
                    entries.Add(["method", $"{t.Name}.{m.Name}", m.Signature, $"files/{f.Slug}.html"]);
                }
            }
        }

        var json = JsonSerializer.Serialize(entries);
        File.WriteAllText(path, "window.ARCH_SEARCH_INDEX = " + json + ";\n", new UTF8Encoding(false));
    }
}
