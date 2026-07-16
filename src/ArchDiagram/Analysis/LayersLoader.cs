using System.Text.Json;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Loads an optional <c>archdiagram.layers.json</c> sidecar from the source root: an
/// ordered (top-to-bottom) list of architectural layers and the namespace/module prefixes that
/// belong to each. When absent or malformed, returns an empty list and the Layering page falls
/// back to an inferred layering. No throw on bad input — a diagnostic is recorded instead.</summary>
public static class LayersLoader
{
    private sealed record LayerDto(string? name, List<string>? namespaces);

    public static List<LayerDef> Load(string sourceRoot, List<string> diagnostics)
    {
        var path = Path.Combine(sourceRoot, "archdiagram.layers.json");
        if (!File.Exists(path)) { return []; }
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dtos = JsonSerializer.Deserialize<List<LayerDto>>(File.ReadAllText(path), opts) ?? [];
            var layers = dtos
                .Where(d => !string.IsNullOrWhiteSpace(d.name))
                .Select(d => new LayerDef(d.name!.Trim(), (d.namespaces ?? []).Where(n => n.Length > 0).ToList()))
                .ToList();
            if (layers.Count == 0) { diagnostics.Add("archdiagram.layers.json contained no usable layers."); }
            return layers;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            diagnostics.Add($"Could not read archdiagram.layers.json: {ex.Message}");
            return [];
        }
    }
}
