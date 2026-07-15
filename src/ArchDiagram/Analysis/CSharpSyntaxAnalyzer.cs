// Tier-2 C# analysis: Roslyn SYNTAX-ONLY parsing (no MSBuild, no compilation,
// no semantic model) so it works on any dropped-in folder that may not build.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ArchDiagram.Scanning;

namespace ArchDiagram.Analysis;

public sealed class CSharpSyntaxAnalyzer : ILanguageAnalyzer
{
    public bool CanHandle(string extension) => extension is ".cs";

    public FileFacts Analyze(FileEntry file, string content)
    {
        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetCompilationUnitRoot();

        var imports = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Where(u => u.Alias is null && u.Name is not null)
            .Select(u => u.Name!.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        var types = new List<Graph.TypeInfo>();
        foreach (var decl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var ns = GetNamespace(decl);
            var kind = decl switch
            {
                ClassDeclarationSyntax => "class",
                StructDeclarationSyntax => "struct",
                InterfaceDeclarationSyntax => "interface",
                RecordDeclarationSyntax r => r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record",
                EnumDeclarationSyntax => "enum",
                _ => "type",
            };

            var methods = new List<Graph.MethodInfo>();
            if (decl is TypeDeclarationSyntax typeDecl)
            {
                foreach (var m in typeDecl.Members.OfType<MethodDeclarationSyntax>())
                {
                    methods.Add(new Graph.MethodInfo
                    {
                        Name = m.Identifier.Text,
                        Arity = m.ParameterList.Parameters.Count,
                        Signature = BuildSignature(m),
                        XmlSummary = GetXmlSummary(m),
                        Cyclomatic = ComplexityMetrics.Cyclomatic(m),
                        Cognitive = ComplexityMetrics.Cognitive(m),
                        StartLine = LineOf(m, first: true),
                        EndLine = LineOf(m, first: false),
                        Invocations = CollectInvocations(m),
                    });
                }
                foreach (var c in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
                {
                    methods.Add(new Graph.MethodInfo
                    {
                        Name = c.Identifier.Text,
                        Arity = c.ParameterList.Parameters.Count,
                        Signature = $"{c.Identifier.Text}({string.Join(", ", c.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "?"))}) (constructor)",
                        XmlSummary = GetXmlSummary(c),
                        Cyclomatic = ComplexityMetrics.Cyclomatic(c),
                        Cognitive = ComplexityMetrics.Cognitive(c),
                        StartLine = LineOf(c, first: true),
                        EndLine = LineOf(c, first: false),
                        Invocations = CollectInvocations(c),
                    });
                }
            }

            types.Add(new Graph.TypeInfo
            {
                Name = decl.Identifier.Text,
                Kind = kind,
                Namespace = ns,
                Modifiers = decl.Modifiers.ToString(),
                BaseTypes = decl.BaseList?.Types.Select(t => t.Type.ToString()).ToList() ?? [],
                XmlSummary = GetXmlSummary(decl),
                Methods = methods,
            });
        }

        return new FileFacts { Language = "C#", Imports = imports, Types = types };
    }

    private static string GetNamespace(SyntaxNode node)
    {
        for (var cur = node.Parent; cur is not null; cur = cur.Parent)
        {
            if (cur is BaseNamespaceDeclarationSyntax ns) { return ns.Name.ToString(); }
        }
        return "";
    }

    /// <summary>1-based first or last source line of a declaration (Roslyn spans are 0-based).</summary>
    private static int LineOf(SyntaxNode node, bool first)
    {
        var span = node.GetLocation().GetLineSpan();
        var pos = first ? span.StartLinePosition : span.EndLinePosition;
        return pos.Line + 1;
    }

    private static string BuildSignature(MethodDeclarationSyntax m) =>
        $"{m.ReturnType} {m.Identifier.Text}({string.Join(", ", m.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.Text}".Trim()))})";

    private static List<Graph.InvocationRef> CollectInvocations(SyntaxNode body) =>
        body.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(inv => new Graph.InvocationRef(InvokedName(inv.Expression), inv.ArgumentList.Arguments.Count))
            .Where(r => r.Name.Length > 0)
            .Distinct()
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Arity)
            .ToList();

    private static string InvokedName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax ma => InvokedName(ma.Name),
        GenericNameSyntax g => g.Identifier.Text,
        MemberBindingExpressionSyntax mb => InvokedName(mb.Name),
        _ => "",
    };

    private static string GetXmlSummary(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();
        var summary = trivia?.ChildNodes().OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.ToString() == "summary");
        if (summary is null) { return ""; }

        var text = string.Join(" ", summary.Content.Select(c => c.ToString()));
        text = text.Replace("///", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }
}
