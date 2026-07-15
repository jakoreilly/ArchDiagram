using System.Text;
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

    public static void Write(ProjectModel model, string path)
    {
        var json = JsonSerializer.Serialize(model, Options);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
