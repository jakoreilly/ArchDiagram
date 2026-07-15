using ArchDiagram.Scanning;

namespace ArchDiagram.Analysis;

/// <summary>Everything a language analyzer could learn from one file.</summary>
public sealed record FileFacts
{
    public required string Language { get; init; }
    public List<string> Imports { get; init; } = [];
    public List<Graph.TypeInfo> Types { get; init; } = [];
}

/// <summary>Tier-1 analyzers are regex-based and language-agnostic-ish (imports only);
/// the Tier-2 C# analyzer additionally extracts types/methods/invocations via Roslyn.</summary>
public interface ILanguageAnalyzer
{
    bool CanHandle(string extension);
    FileFacts Analyze(FileEntry file, string content);
}
