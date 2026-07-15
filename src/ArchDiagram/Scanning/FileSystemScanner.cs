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
        // Canonical (resolved) directory paths already visited — guards against symlink /
        // junction cycles that would otherwise recurse forever and blow the stack.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(root, root, skips, results, diagnostics, visited);
        results.Sort((a, b) => string.Compare(a.RelPath, b.RelPath, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    private static void Walk(string dir, string root, HashSet<string> skips, List<FileEntry> results, List<string>? diagnostics, HashSet<string> visited)
    {
        try
        {
            var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(new DirectoryInfo(dir).FullName));
            if (!visited.Add(canonical)) { return; } // already walked (cycle) — stop
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return; }

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
                // Skip reparse points (symlinks/junctions) outright — they are the usual
                // source of scan cycles and duplicated subtrees.
                if (IsReparsePoint(entry)) { continue; }
                if (!skips.Contains(Path.GetFileName(entry))) { Walk(entry, root, skips, results, diagnostics, visited); }
                continue;
            }

            long size;
            try { size = new FileInfo(entry).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            var rel = Path.GetRelativePath(root, entry).Replace('\\', '/');
            results.Add(new FileEntry(entry, rel, Path.GetExtension(entry).ToLowerInvariant(), size));
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
