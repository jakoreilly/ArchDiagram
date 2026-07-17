using System.Text.Json;
using ArchDiagram.Graph;

namespace ArchDiagram.Rendering;

public static class ModelJsonWriter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Streams directly to the file instead of buffering the whole (potentially
    /// multi-MB, since model.json is a complete archive) JSON string in memory first.
    /// Utf8JsonWriter always emits UTF-8 with no BOM, matching the previous
    /// UTF8Encoding(false) exactly, so output is byte-identical.</summary>
    public static void Write(ProjectModel model, string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        JsonSerializer.Serialize(stream, model, Options);
    }
}
