namespace ArchDiagram.Site;

/// <summary>Copies the viewer assets (css, js, mermaid lib) that ship next to the
/// exe into a generated site's assets/ folder. Shared by SiteGenerator and the
/// Landscape generator.</summary>
public static class SiteAssets
{
    public static void CopyTo(string outDir)
    {
        var src = Path.Combine(AppContext.BaseDirectory, "assets");
        if (!Directory.Exists(src))
        {
            throw new DirectoryNotFoundException($"Viewer assets not found at '{src}' — build output is incomplete.");
        }
        var dest = Path.Combine(outDir, "assets");
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
