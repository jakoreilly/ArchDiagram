using System.Text.RegularExpressions;
using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Finds TODO/FIXME/HACK/BUG/XXX markers in source comments. Line-based and
/// cheap: a marker only counts when it follows a real comment-lead token
/// (<c>//</c>, <c>/*</c>, block-continuation <c>*</c>, <c>#</c>, <c>--</c>, <c>&lt;!--</c>),
/// and — when the file's language is known — only a token that language actually uses.
/// This keeps identifiers like <c>todoList</c>, string literals like <c>"BUG"</c> and
/// YAML/Markdown keys out of the debt list.</summary>
public static partial class TodoScanner
{
    private const int MaxPerFile = 50;
    private const int MaxTextLength = 200;

    // Group 1 = comment-lead token, 2 = tag, 3 = text. Ordered alternation so multi-char
    // tokens win over their prefixes (<!-- before --, /* before *). No bare ^ / ' anchors —
    // those produced the false positives. Timeout guards against pathological lines.
    [GeneratedRegex(@"(?<tok><!--|//|/\*|--|#|\*)[^\w]*\b(?<tag>TODO|FIXME|HACK|XXX|BUG|UNDONE)\b[:\s\-]*(?<txt>.*)",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex Marker();

    // Comment-lead tokens each language actually uses. Unknown/empty language ⇒ permissive
    // (every token), so callers that don't pass a language still work.
    private static readonly string[] AllTokens = ["<!--", "//", "/*", "--", "#", "*"];

    private static string[] TokensFor(string language) => language switch
    {
        "C#" or "TypeScript/JavaScript" or "Razor" => ["//", "/*", "*"],
        "SQL" => ["--", "/*", "*"],
        "Python" or "PowerShell" or "YAML" or "Shell" => ["#"],
        "HTML" or "XML" or "Markdown" => ["<!--"],
        _ => AllTokens,
    };

    /// <param name="language">The file's language (see <c>KnownFileAnalyzer.LanguageByExtension</c>).
    /// Empty/unknown ⇒ any comment token is accepted.</param>
    public static List<TodoItem> Scan(string content, string language = "")
    {
        var result = new List<TodoItem>();
        if (content.Length == 0) { return result; }

        var allowed = new HashSet<string>(TokensFor(language), StringComparer.Ordinal);

        var line = 0;
        foreach (var raw in content.Split('\n'))
        {
            line++;
            if (result.Count >= MaxPerFile) { break; }
            // Fast pre-filter before regex — all case-insensitive so lowercase `bug`/`xxx`
            // are not silently dropped (they would match the IgnoreCase regex otherwise).
            if (raw.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("FIXME", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("HACK", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("XXX", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("BUG", StringComparison.OrdinalIgnoreCase) < 0 &&
                raw.IndexOf("UNDONE", StringComparison.OrdinalIgnoreCase) < 0) { continue; }

            Match m;
            try { m = Marker().Match(raw.TrimEnd('\r')); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!m.Success) { continue; }
            if (!allowed.Contains(m.Groups["tok"].Value)) { continue; }

            var text = m.Groups["txt"].Value.Trim().TrimEnd('*', '/', '-', '>').Trim();
            var (author, cleaned) = ExtractAuthorOrTicket(text);
            if (cleaned.Length > MaxTextLength) { cleaned = cleaned[..MaxTextLength] + "…"; }
            result.Add(new TodoItem(line, m.Groups["tag"].Value.ToUpperInvariant(), cleaned, author));
        }
        return result;
    }

    // F6: pull a leading/trailing "(name)" or "#123" out of the marker text so the debt
    // list can attribute it. Returns (attribution, text-without-attribution).
    private static (string Author, string Text) ExtractAuthorOrTicket(string text)
    {
        var mParen = LeadingParen().Match(text);
        if (mParen.Success)
        {
            return (mParen.Groups[1].Value.Trim(), text[mParen.Length..].TrimStart(' ', ':', '-').Trim());
        }
        var mTicket = Ticket().Match(text);
        if (mTicket.Success) { return (mTicket.Value, text); }
        return ("", text);
    }

    [GeneratedRegex(@"^\(([^)]{1,40})\)", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex LeadingParen();

    [GeneratedRegex(@"#\d{1,7}\b", RegexOptions.None, matchTimeoutMilliseconds: 200)]
    private static partial Regex Ticket();
}
