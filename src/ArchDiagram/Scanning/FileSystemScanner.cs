namespace ArchDiagram.Scanning;

public sealed record FileEntry(string AbsPath, string RelPath, string Extension, long SizeBytes);

/// <summary>Walks the source tree once, skipping build/VCS/vendor folders, and
/// returns every file ArchDiagram might care about, sorted by relative path.</summary>
public static class FileSystemScanner
{
    public static readonly string[] DefaultSkipDirNames =
        ["bin", "obj", ".git", ".vs", ".vscode", ".idea", ".archforge", "node_modules", "packages", "dist", "build", "out", "__pycache__", ".venv", "venv", "TestResults"];

    public static List<FileEntry> Scan(string root, IReadOnlyCollection<string>? extraSkips = null, List<string>? diagnostics = null)
    {
        var skips = new HashSet<string>(DefaultSkipDirNames, StringComparer.OrdinalIgnoreCase);
        foreach (var s in extraSkips ?? []) { skips.Add(s); }

        var results = new List<FileEntry>();
        Walk(root, root, skips, results, diagnostics);
        results.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static void Walk(string dir, string root, HashSet<string> skips, List<FileEntry> results, List<string>? diagnostics)
    {
        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics?.Add($"Could not read directory '{Path.GetRelativePath(root, dir)}': {ex.Message}");
            return;
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
            {
                if (!skips.Contains(Path.GetFileName(entry))) { Walk(entry, root, skips, results, diagnostics); }
                continue;
            }

            long size;
            try { size = new FileInfo(entry).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var rel = Path.GetRelativePath(root, entry).Replace('\\', '/');
            results.Add(new FileEntry(entry, rel, Path.GetExtension(entry).ToLowerInvariant(), size));
        }
    }
}
