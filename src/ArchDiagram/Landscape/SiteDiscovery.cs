using System.Text.Json;
using ArchDiagram.Graph;

namespace ArchDiagram.Landscape;

/// <summary>Finds every immediate subfolder of the parent dir that contains a
/// model.json, loads it, and returns a SiteRef with a relative href from the
/// landscape output dir. Unreadable/unparseable sites are skipped with a diagnostic.</summary>
public static class SiteDiscovery
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static List<SiteRef> Discover(string parentDir, string landscapeOutDir, List<string> diagnostics, ISet<string>? onlyFolderNames = null)
    {
        var sites = new List<SiteRef>();
        var outFull = Path.GetFullPath(landscapeOutDir);
        foreach (var dir in Directory.EnumerateDirectories(parentDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (Path.GetFullPath(dir).Equals(outFull, StringComparison.OrdinalIgnoreCase)) { continue; } // skip our own output
            var id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (onlyFolderNames is not null && !onlyFolderNames.Contains(id)) { continue; } // scope to a group's subset
            var jsonPath = Path.Combine(dir, "model.json");
            if (!File.Exists(jsonPath)) { continue; }
            try
            {
                var model = JsonSerializer.Deserialize<ProjectModel>(File.ReadAllText(jsonPath), ReadOptions);
                if (model is null) { diagnostics.Add($"Skipped {jsonPath}: deserialized to null."); continue; }
                var href = Path.GetRelativePath(landscapeOutDir, Path.Combine(dir, "index.html")).Replace('\\', '/');
                sites.Add(new SiteRef(id, model, href));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Skipped {jsonPath}: {ex.Message}");
            }
        }
        return sites;
    }
}
