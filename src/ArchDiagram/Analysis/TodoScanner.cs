using System.Text.RegularExpressions;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Finds TODO/FIXME/HACK/BUG/XXX markers in source comments. Line-based
/// regex — cheap and language-agnostic; only lines that look like comments (or
/// contain the marker after a comment token) are matched to limit false positives.</summary>
public static partial class TodoScanner
{
    private const int MaxPerFile = 50;
    private const int MaxTextLength = 200;

    [GeneratedRegex(@"(?:^|//|#|<!--|/\*|--|\*|')[^\w]*\b(TODO|FIXME|HACK|XXX|BUG|UNDONE)\b[:\s\-]*(.*)", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    public static List<TodoItem> Scan(string content)
    {
        var result = new List<TodoItem>();
        if (content.Length == 0) { return result; }

        var line = 0;
        foreach (var raw in content.Split('\n'))
        {
            line++;
            if (result.Count >= MaxPerFile) { break; }
            // Fast pre-filter before regex.
            if (raw.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("FIXME", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("HACK", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("XXX", StringComparison.Ordinal) < 0 &&
                raw.IndexOf("BUG", StringComparison.Ordinal) < 0 &&
                raw.IndexOf("UNDONE", StringComparison.Ordinal) < 0) { continue; }

            var m = Marker().Match(raw.TrimEnd('\r'));
            if (!m.Success) { continue; }
            var text = m.Groups[2].Value.Trim().TrimEnd('*', '/', '-', '>').Trim();
            if (text.Length > MaxTextLength) { text = text[..MaxTextLength] + "…"; }
            result.Add(new TodoItem(line, m.Groups[1].Value.ToUpperInvariant(), text));
        }
        return result;
    }
}
