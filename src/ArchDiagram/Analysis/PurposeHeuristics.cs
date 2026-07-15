using System.Text.RegularExpressions;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Derives a one-line "purpose" for each file — heuristically, never by
/// guessing beyond the evidence. Priority: file-head comment > first XML summary
/// > name-convention table > "defines X, Y" fallback. PurposeSource records which
/// rule fired so the UI can badge it honestly.</summary>
public static class PurposeHeuristics
{
    private static readonly (string Suffix, string Purpose)[] NameConventions =
    [
        ("Controller", "HTTP endpoint controller"),
        ("Service", "application/business service"),
        ("Repository", "data access repository"),
        ("Tests", "automated test suite"),
        ("Test", "automated test suite"),
        ("Factory", "object factory"),
        ("Builder", "builder"),
        ("Handler", "message/event handler"),
        ("Middleware", "request pipeline middleware"),
        ("Options", "configuration options"),
        ("Settings", "configuration settings"),
        ("Extensions", "extension methods"),
        ("Helper", "helper utilities"),
        ("Helpers", "helper utilities"),
        ("Utils", "helper utilities"),
        ("Validator", "input validation"),
        ("Client", "external service client"),
        ("Renderer", "output renderer"),
        ("Scanner", "scanner"),
        ("Parser", "parser"),
        ("Analyzer", "analyzer"),
        ("Page", "page/view"),
        ("ViewModel", "view model"),
        ("Model", "data model"),
        ("Models", "data models"),
    ];

    public static void Apply(FileNode file, string content)
    {
        var (purpose, source) = Derive(file, content);
        file.Purpose = purpose;
        file.PurposeSource = source;
    }

    private static (string Purpose, string Source) Derive(FileNode file, string content)
    {
        var head = HeadComment(file.Language, content);
        if (head.Length > 0) { return (Truncate(head), "file-head comment"); }

        var xml = file.Types.Select(t => t.XmlSummary).FirstOrDefault(s => s.Length > 0);
        if (!string.IsNullOrEmpty(xml)) { return (Truncate(xml!), "XML doc summary"); }

        var fileName = Path.GetFileName(file.RelPath);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)) { return ("Application entry point.", "name convention"); }
        if (fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase)) { return ("Application startup/configuration.", "name convention"); }
        if (fileName.Contains(".config.", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
        { return ("Configuration file.", "name convention"); }
        if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase)) { return ("Project documentation.", "name convention"); }

        foreach (var (suffix, purpose) in NameConventions)
        {
            if (stem.EndsWith(suffix, StringComparison.Ordinal))
            {
                return ($"{Capitalize(purpose)} ({stem}).", "name convention");
            }
        }

        if (file.Types.Count > 0)
        {
            var names = file.Types.Take(3).Select(t => t.Name).ToList();
            var more = file.Types.Count > 3 ? $" and {file.Types.Count - 3} more" : "";
            return ($"{file.Language} source defining {string.Join(", ", names)}{more}.", "declared types");
        }

        return ($"{file.Language} file.", "language only");
    }

    private static string HeadComment(string language, string content)
    {
        var lines = content.Split('\n').Take(10).Select(l => l.TrimEnd('\r')).ToList();
        var (linePrefix, blockStart) = language switch
        {
            "C#" or "TypeScript/JavaScript" => ("//", "/*"),
            "Python" or "PowerShell" or "YAML" or "Shell" => ("#", "<#"),
            "SQL" => ("--", "/*"),
            _ => ("", ""),
        };
        if (linePrefix.Length == 0) { return ""; }

        var comment = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0) { if (comment.Count > 0) { break; } continue; }
            if (t.StartsWith(linePrefix, StringComparison.Ordinal) && !t.StartsWith(linePrefix + "/", StringComparison.Ordinal))
            {
                var text = t[linePrefix.Length..].Trim();
                if (text.Length > 0) { comment.Add(text); }
                if (comment.Count >= 2) { break; }
                continue;
            }
            if (blockStart.Length > 0 && t.StartsWith(blockStart, StringComparison.Ordinal))
            {
                var text = t[blockStart.Length..].Trim(' ', '*', '#', '>');
                if (text.Length > 0) { comment.Add(text); }
                break;
            }
            break;
        }

        var joined = string.Join(" ", comment).Trim();
        // Skip license/shebang/pragma noise.
        if (Regex.IsMatch(joined, @"^(copyright|licensed|#!|<auto-generated)", RegexOptions.IgnoreCase)) { return ""; }
        return joined;
    }

    private static string Truncate(string s) => s.Length <= 180 ? s : s[..177] + "...";
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
