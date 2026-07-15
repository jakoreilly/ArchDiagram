using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

public static class StructurePage
{
    private sealed class Folder
    {
        public SortedDictionary<string, Folder> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<FileNode> Files { get; } = [];
        public int TotalFiles;
        public long TotalBytes;
    }

    public static string Body(ProjectModel model)
    {
        var root = new Folder();
        foreach (var file in model.Files)
        {
            var parts = file.RelPath.Split('/');
            var cur = root;
            cur.TotalFiles++; cur.TotalBytes += file.SizeBytes;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!cur.Folders.TryGetValue(parts[i], out var next)) { cur.Folders[parts[i]] = next = new Folder(); }
                cur = next;
                cur.TotalFiles++; cur.TotalBytes += file.SizeBytes;
            }
            cur.Files.Add(file);
        }

        var sb = new StringBuilder();
        sb.Append("<h1>Folder &amp; File Structure</h1>");
        sb.Append("""
<p class="lede">Every folder and file found in the scan (build outputs, VCS folders and vendor
directories are skipped). Expand folders to explore; click any file to open its detail page.
Hover a file for its purpose summary.</p>
""");
        // Visual treemap: fast "where is the mass" orientation before the text tree.
        var treemap = TreemapRenderer.Render(model.Files);
        if (treemap.Length > 0)
        {
            sb.Append("<h2>Codebase map <span class=\"badge\">sized by lines of code</span></h2>");
            sb.Append("<p class=\"lede\">Every file as a rectangle — bigger = more lines, colour = language, grouped by "
                    + "top-level folder. Hover for details, click to open a file. The full tree is below.</p>");
            sb.Append($"<div class=\"treemap\">{treemap}</div>");
            sb.Append("<p class=\"note\">Files under a few lines may be too small to label; use the tree or search to find them. "
                    + "Test files are excluded from this map — use the 🧪 Tests toggle (bottom of the sidebar) to show them in the tree.</p>");
        }

        if (GraphPage.HasData(model)) { sb.Append(PageTemplate.ExploreIn3DNote()); }
        sb.Append("""
<div class="select-row">
  <input type="text" class="filter-input" id="tree-filter" placeholder="Filter files by name or path…" autocomplete="off" spellcheck="false">
  <button class="btn" id="tree-expand" type="button">Expand all</button>
  <button class="btn" id="tree-collapse" type="button">Collapse all</button>
  <span class="filter-count"></span>
</div>
""");
        sb.Append("<div class=\"panel tree\" id=\"structure-tree\">");
        if (model.Files.Count == 0)
        {
            sb.Append("<div class=\"empty-state\"><div class=\"big\">🗀</div><p>No files were found in the scanned folder.</p></div>");
        }
        // Per top-level folder "what lives here" blurb (deterministic, from purposes).
        var byTopFolder = model.Files
            .Where(f => f.RelPath.Contains('/'))
            .GroupBy(f => f.RelPath[..f.RelPath.IndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FileNode>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (name, folder) in root.Folders)
        {
            // Authored folder description (sidecar) wins over the generated blurb.
            var blurb = model.FolderDescriptions.TryGetValue(name, out var authored) && authored.Length > 0
                ? authored
                : byTopFolder.TryGetValue(name, out var fs) ? Analysis.NarrativeBuilder.FolderBlurb(name, fs) : "";
            AppendFolder(sb, name, folder, depth: 0, blurb);
        }
        AppendFiles(sb, root.Files);
        sb.Append("</div>");
        return sb.ToString();
    }

    private static void AppendFolder(StringBuilder sb, string name, Folder folder, int depth, string blurb = "")
    {
        var open = depth < 2 ? " open" : "";
        sb.Append($"<details{open}><summary>🗀 {Html.Encode(name)}<span class=\"meta\">{folder.TotalFiles:N0} files · {FormatBytes(folder.TotalBytes)}</span></summary>");
        if (blurb.Length > 0) { sb.Append($"<p class=\"note\" style=\"margin:.4rem 0\">{Html.Encode(blurb)}</p>"); }
        foreach (var (childName, child) in folder.Folders)
        {
            AppendFolder(sb, childName, child, depth + 1);
        }
        AppendFiles(sb, folder.Files);
        sb.Append("</details>");
    }

    private static void AppendFiles(StringBuilder sb, List<FileNode> files)
    {
        if (files.Count == 0) { return; }
        sb.Append("<ul>");
        foreach (var f in files.OrderBy(f => f.RelPath, StringComparer.OrdinalIgnoreCase))
        {
            var name = f.RelPath.Split('/')[^1];
            var title = f.Purpose.Length > 0 ? $" title=\"{Html.Encode(f.Purpose)}\"" : "";
            var testAttr = f.IsTest ? " data-test=\"1\"" : "";
            sb.Append($"<li data-path=\"{Html.Encode(f.RelPath.ToLowerInvariant())}\"{testAttr}><a href=\"files/{f.Slug}.html\"{title}>{Html.Encode(name)}</a>" +
                      $"<span class=\"file-lang\">{Html.Encode(f.Language)} · {FormatBytes(f.SizeBytes)}</span></li>");
        }
        sb.Append("</ul>");
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB",
    };
}
