using System.Text.RegularExpressions;
using ArchDiagram.Scanning;

namespace ArchDiagram.Analysis;

/// <summary>Tier-1 import extraction. Each analyzer returns raw import strings;
/// ImportResolver later maps them back to files in the scanned set.</summary>
public abstract class RegexImportAnalyzer : ILanguageAnalyzer
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    public abstract bool CanHandle(string extension);
    public abstract string Language { get; }
    protected abstract IEnumerable<string> FindImports(string content);

    public FileFacts Analyze(FileEntry file, string content)
    {
        List<string> imports;
        try
        {
            imports = FindImports(content)
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(i => i, StringComparer.Ordinal)
                .ToList();
        }
        catch (RegexMatchTimeoutException) { imports = []; }
        return new FileFacts { Language = Language, Imports = imports };
    }

    protected static IEnumerable<string> MatchGroup1(Regex regex, string content)
    {
        foreach (Match m in regex.Matches(content))
        {
            for (var g = 1; g < m.Groups.Count; g++)
            {
                if (m.Groups[g].Success) { yield return m.Groups[g].Value.Trim(); break; }
            }
        }
    }
}

public sealed class CSharpUsingAnalyzer : RegexImportAnalyzer
{
    private static readonly Regex Using = new(@"^\s*(?:global\s+)?using\s+(?:static\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline, Timeout);

    public override string Language => "C#";
    public override bool CanHandle(string extension) => extension is ".cs";
    protected override IEnumerable<string> FindImports(string content) =>
        MatchGroup1(Using, content).Where(u => !u.Contains('='));
}

public sealed class TsJsImportAnalyzer : RegexImportAnalyzer
{
    private static readonly Regex EsImport = new(@"^\s*(?:import|export)\s+(?:[^'""]*?\s+from\s+)?['""]([^'""]+)['""]",
        RegexOptions.Compiled | RegexOptions.Multiline, Timeout);
    private static readonly Regex Require = new(@"require\s*\(\s*['""]([^'""]+)['""]\s*\)",
        RegexOptions.Compiled, Timeout);

    public override string Language => "TypeScript/JavaScript";
    public override bool CanHandle(string extension) => extension is ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs";
    protected override IEnumerable<string> FindImports(string content) =>
        MatchGroup1(EsImport, content).Concat(MatchGroup1(Require, content));
}

public sealed class PythonImportAnalyzer : RegexImportAnalyzer
{
    private static readonly Regex Import = new(@"^\s*(?:from\s+([A-Za-z_][A-Za-z0-9_.]*)\s+import|import\s+([A-Za-z_][A-Za-z0-9_.]*))",
        RegexOptions.Compiled | RegexOptions.Multiline, Timeout);

    public override string Language => "Python";
    public override bool CanHandle(string extension) => extension is ".py";
    protected override IEnumerable<string> FindImports(string content) => MatchGroup1(Import, content);
}

public sealed class PowerShellImportAnalyzer : RegexImportAnalyzer
{
    private static readonly Regex DotSource = new(@"^\s*\.\s+['""]?(\$?[^\s'""#]+\.ps1)['""]?",
        RegexOptions.Compiled | RegexOptions.Multiline, Timeout);
    private static readonly Regex ImportModule = new(@"(?im)^\s*(?:Import-Module|using\s+module)\s+['""]?([^\s'""#;]+)['""]?",
        RegexOptions.Compiled, Timeout);

    public override string Language => "PowerShell";
    public override bool CanHandle(string extension) => extension is ".ps1" or ".psm1" or ".psd1";
    protected override IEnumerable<string> FindImports(string content) =>
        MatchGroup1(DotSource, content).Concat(MatchGroup1(ImportModule, content));
}

/// <summary>Catch-all for languages/files we recognise but don't extract imports from.</summary>
public sealed class KnownFileAnalyzer : ILanguageAnalyzer
{
    public static readonly Dictionary<string, string> LanguageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#", [".csproj"] = "MSBuild", [".sln"] = "Solution", [".slnx"] = "Solution",
        [".ts"] = "TypeScript/JavaScript", [".tsx"] = "TypeScript/JavaScript", [".js"] = "TypeScript/JavaScript",
        [".jsx"] = "TypeScript/JavaScript", [".mjs"] = "TypeScript/JavaScript", [".cjs"] = "TypeScript/JavaScript",
        [".py"] = "Python", [".ps1"] = "PowerShell", [".psm1"] = "PowerShell", [".psd1"] = "PowerShell",
        [".sql"] = "SQL", [".html"] = "HTML", [".css"] = "CSS", [".scss"] = "CSS",
        [".json"] = "JSON", [".yaml"] = "YAML", [".yml"] = "YAML", [".xml"] = "XML", [".config"] = "XML",
        [".md"] = "Markdown", [".cmd"] = "Batch", [".bat"] = "Batch", [".sh"] = "Shell",
        [".proto"] = "Protobuf", [".razor"] = "Razor", [".cshtml"] = "Razor", [".vb"] = "VB.NET",
        [".fs"] = "F#", [".go"] = "Go", [".java"] = "Java", [".rb"] = "Ruby", [".php"] = "PHP",
        [".rs"] = "Rust", [".c"] = "C/C++", [".cpp"] = "C/C++", [".h"] = "C/C++", [".hpp"] = "C/C++",
        [".toml"] = "TOML", [".ini"] = "INI", [".txt"] = "Text", [".dockerfile"] = "Docker",
    };

    public bool CanHandle(string extension) => LanguageByExtension.ContainsKey(extension);

    public FileFacts Analyze(FileEntry file, string content) =>
        new() { Language = LanguageByExtension.GetValueOrDefault(file.Extension, "Other") };
}
