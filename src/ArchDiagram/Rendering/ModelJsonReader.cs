using System.Text.Json;
using ArchDiagram.Graph;

namespace ArchDiagram.Rendering;

/// <summary>Loads a previously-written <c>model.json</c> back into a <see cref="ProjectModel"/>,
/// using the same options as <see cref="ModelJsonWriter"/>. Because the whole site is generated
/// purely from the model, a single <c>model.json</c> is a complete, compact archive: feed it back
/// and the entire site rebuilds without the original source.</summary>
public static class ModelJsonReader
{
    public static ProjectModel Read(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ProjectModel>(json, ModelJsonWriter.Options)
            ?? throw new InvalidDataException($"'{path}' did not contain a valid ArchDiagram model.");
    }
}
