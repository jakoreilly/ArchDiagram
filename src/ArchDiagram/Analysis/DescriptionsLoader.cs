using System.Text.Json;

namespace ArchDiagram.Analysis;

/// <summary>Author-written descriptions loaded from an optional
/// <c>archdiagram.descriptions.json</c> sidecar. When present, these override the heuristic
/// <c>Purpose</c> and add an "About this project" panel; when absent, analysis falls back to the
/// generated heuristics. Paths are source-root-relative, forward-slash, case-insensitive; a key
/// ending in "/" is a folder description, otherwise an exact file.</summary>
public sealed record AuthoredDescriptions(
    string Project,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, string> Folders)
{
    public static readonly AuthoredDescriptions Empty =
        new("", new Dictionary<string, string>(), new Dictionary<string, string>());

    public bool IsEmpty => Project.Length == 0 && Files.Count == 0 && Folders.Count == 0;
}

public static class DescriptionsLoader
{
    public const string DefaultFileName = "archdiagram.descriptions.json";

    private sealed class Doc
    {
        public string? Project { get; set; }
        public Dictionary<string, string>? Files { get; set; }
    }

    /// <summary>Loads descriptions from <paramref name="explicitPath"/>, or from
    /// <c>&lt;sourceRoot&gt;/archdiagram.descriptions.json</c> when null. A missing default file is
    /// normal (returns empty, no diagnostic); a missing explicit file or malformed JSON adds a
    /// diagnostic and returns empty — never throws.</summary>
    public static AuthoredDescriptions Load(string? explicitPath, string sourceRoot, List<string> diagnostics)
    {
        var path = explicitPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(sourceRoot, DefaultFileName);
            if (!File.Exists(path)) { return AuthoredDescriptions.Empty; }   // absence is normal
        }
        else if (!File.Exists(path))
        {
            diagnostics.Add($"Descriptions file not found: {path}");
            return AuthoredDescriptions.Empty;
        }

        Doc? doc;
        try
        {
            doc = JsonSerializer.Deserialize<Doc>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"Could not read descriptions file ({path}): {ex.Message}");
            return AuthoredDescriptions.Empty;
        }
        if (doc is null) { return AuthoredDescriptions.Empty; }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in doc.Files ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) { continue; }
            var norm = key.Replace('\\', '/').TrimStart('.', '/');
            if (norm.EndsWith('/')) { folders[norm.TrimEnd('/')] = value.Trim(); }
            else { files[norm] = value.Trim(); }
        }

        return new AuthoredDescriptions((doc.Project ?? "").Trim(), files, folders);
    }
}
