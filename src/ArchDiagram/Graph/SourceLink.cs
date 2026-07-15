namespace ArchDiagram.Graph;

/// <summary>How to turn a repo-relative file path (+ optional line) into a
/// clickable source URL. Serialized into model.json so the offline viewer can
/// build links client-side; null when the user configured no source.</summary>
public sealed record SourceLink
{
    /// <summary>"github" | "gitlab" | "local" | "none".</summary>
    public required string Type { get; init; }

    /// <summary>Repo/web base ("https://github.com/org/repo") or a local root
    /// ("C:/src/app" or "file:///C:/src/app"). No trailing slash required.</summary>
    public string Base { get; init; } = "";

    /// <summary>Branch/tag/commit for web hosts; ignored for local.</summary>
    public string Ref { get; init; } = "main";

    /// <summary>Builds a URL for a repo-relative path (forward slashes) and an
    /// optional 1-based line (0 = no line anchor). Pure + deterministic; unit-tested.
    /// Returns "" when no usable link can be formed.</summary>
    public string UrlFor(string relPath, int line = 0)
    {
        if (string.IsNullOrWhiteSpace(Base) || string.IsNullOrWhiteSpace(relPath)) { return ""; }
        var path = relPath.Replace('\\', '/').TrimStart('/');
        var b = Base.TrimEnd('/');
        return Type switch
        {
            "github" => $"{b}/blob/{Ref}/{path}" + (line > 0 ? $"#L{line}" : ""),
            "gitlab" => $"{b}/-/blob/{Ref}/{path}" + (line > 0 ? $"#L{line}" : ""),
            "local" => (b.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? b : "file:///" + b.Replace(" ", "%20"))
                        + "/" + path, // file:// cannot deep-link a line
            _ => "",
        };
    }
}
