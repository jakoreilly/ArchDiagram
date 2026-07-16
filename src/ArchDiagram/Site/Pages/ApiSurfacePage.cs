using System.Text;
using ArchDiagram.Graph;

namespace ArchDiagram.Site.Pages;

/// <summary>Public API surface — the contract other code can depend on — grouped by namespace,
/// plus "critical paths": how execution/dependencies reach the most central files. Public types
/// (and, for classes, their public methods; interface members are public by definition) are what
/// a reviewer treats as the stable boundary of each module. First-party code only.</summary>
public static class ApiSurfacePage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>API Surface</h1>");
        sb.Append("<p class=\"lede\">The <strong>public contract</strong> of the codebase: public types grouped by "
                + "namespace, and their public members. This is what other modules (and consumers) can depend on — the "
                + "surface you must keep stable. Private/internal detail is omitted. Tests, fixtures and vendored code are excluded.</p>");

        var files = model.Files.Where(Analysis.CodebaseStats.IsFirstParty).ToList();
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);

        // Public types with the file they live in.
        var publicTypes = files
            .SelectMany(f => f.Types.Where(IsPublicType).Select(t => (File: f, Type: t)))
            .ToList();

        var publicMemberCount = publicTypes.Sum(x => PublicMethods(x.Type).Count);

        sb.Append("<div class=\"tiles\">");
        Tile(sb, publicTypes.Count.ToString("N0"), "Public types");
        Tile(sb, publicMemberCount.ToString("N0"), "Public methods");
        Tile(sb, publicTypes.Select(x => x.Type.Namespace).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Count().ToString("N0"), "Namespaces");
        sb.Append("</div>");

        AppendCriticalPaths(sb, model, bySlug);

        // Public surface grouped by namespace.
        sb.Append("<h2>Public types by namespace</h2>");
        if (publicTypes.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⧉</div>"
                    + "<p>No public types were detected. Either this codebase has no C#, or its types are all internal/private — "
                    + "there is no cross-module public surface to document.</p></div>");
            return sb.ToString();
        }

        var groups = publicTypes
            .GroupBy(x => x.Type.Namespace.Length > 0 ? x.Type.Namespace : "(no namespace)", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            sb.Append($"<h3>{Html.Encode(g.Key)} <span class=\"badge\">{g.Count()} type(s)</span></h3>");
            sb.Append("<div class=\"panel\"><ul class=\"member-list\" style=\"font-family:inherit\">");
            var first = true;
            foreach (var (file, type) in g.OrderBy(x => x.Type.Name, StringComparer.Ordinal))
            {
                var style = first ? " style=\"border-top:none\"" : "";
                first = false;
                var kind = Html.Encode(type.Kind);
                var bases = type.BaseTypes.Count > 0 ? " : " + Html.Encode(string.Join(", ", type.BaseTypes)) : "";
                var methods = PublicMethods(type);
                var memberSummary = methods.Count > 0
                    ? " <span class=\"badge\">" + methods.Count + " public method(s)</span>"
                    : "";
                sb.Append($"<li{style}><span class=\"badge accent\">{kind}</span> "
                        + $"<a href=\"files/{file.Slug}.html\"><strong>{Html.Encode(type.Name)}</strong></a>{Html.Encode(bases)}{memberSummary}");
                if (type.XmlSummary.Length > 0)
                {
                    sb.Append($"<div class=\"note\" style=\"margin:.2rem 0 0\">{Html.Encode(type.XmlSummary)}</div>");
                }
                if (methods.Count > 0)
                {
                    sb.Append("<div style=\"margin:.3rem 0 0;color:var(--text-soft);font-size:.85rem\">");
                    foreach (var m in methods.OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal))
                    {
                        sb.Append($"<div><code>{Html.Encode(m.Signature)}</code></div>");
                    }
                    sb.Append("</div>");
                }
                sb.Append("</li>");
            }
            sb.Append("</ul></div>");
        }
        return sb.ToString();
    }

    /// <summary>Critical paths: for the most central files, the shortest chain from an entry
    /// point (a file nothing imports) that reaches it — the code path a reader follows to get there.</summary>
    private static void AppendCriticalPaths(StringBuilder sb, ProjectModel model, Dictionary<string, FileNode> bySlug)
    {
        var key = Analysis.ImportanceScorer.Rank(model, 8).Where(s => Analysis.CodebaseStats.IsFirstParty(s.File)).ToList();
        sb.Append("<h2>Critical paths <span class=\"badge accent\">to key files</span></h2>");
        if (key.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">↝</div>"
                    + "<p>No dependency links were detected, so there are no code paths to trace yet.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">How the code reaches the files that matter most: the shortest chain of imports from an "
                + "entry point (a file nothing else imports) to each key file. Read left-to-right to follow the dependency path in.</p>");
        sb.Append("<div class=\"panel\"><ul class=\"member-list\" style=\"font-family:inherit\">");
        var first = true;
        foreach (var s in key)
        {
            var style = first ? " style=\"border-top:none\"" : "";
            first = false;
            var path = Analysis.CriticalPaths.ToFile(model, s.File.Slug);
            string body;
            if (path is { Count: > 1 })
            {
                body = string.Join(" <span class=\"crumb-sep\">→</span> ",
                    path.Select(slug => bySlug.TryGetValue(slug, out var f)
                        ? $"<a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath.Split('/')[^1])}</a>"
                        : Html.Encode(slug)));
            }
            else
            {
                body = $"<a href=\"files/{s.File.Slug}.html\">{Html.Encode(s.File.RelPath.Split('/')[^1])}</a> "
                     + "<span class=\"badge\">entry point / root</span>";
            }
            sb.Append($"<li{style}>{body}</li>");
        }
        sb.Append("</ul></div>");
    }

    private static bool IsPublicType(TypeInfo t) =>
        t.Modifiers.Contains("public", StringComparison.Ordinal)
        || t.Kind.Equals("interface", StringComparison.Ordinal) && !t.Modifiers.Contains("internal", StringComparison.Ordinal) && !t.Modifiers.Contains("private", StringComparison.Ordinal);

    /// <summary>Members that form the public surface: for an interface every method is public;
    /// for other types, methods explicitly marked public.</summary>
    private static List<MethodInfo> PublicMethods(TypeInfo t)
    {
        var isInterface = t.Kind.Equals("interface", StringComparison.Ordinal);
        return t.Methods
            .Where(m => (isInterface || m.Modifiers.Contains("public", StringComparison.Ordinal)) && m.Signature.Length > 0)
            .ToList();
    }

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
}
