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
            // A file needs no `using` to reference another type in its OWN namespace (same-
            // namespace lookup is implicit in C#), so that real dependency was otherwise
            // invisible to ImportResolver — add each namespace this file declares a type in
            // as an implicit import. ImportResolver already excludes self-edges (t.Slug !=
            // file.Slug) and already caps same-namespace fan-out at MaxNamespaceEdgesPerImport,
            // so this reuses the existing heuristic precision rather than adding a new edge type.
            .Concat(root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().Select(n => n.Name.ToString()))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();

        var types = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(BuildType).ToList();
        var topLevel = BuildTopLevelType(root);
        if (topLevel is not null) { types.Add(topLevel); }

        return new FileFacts { Language = "C#", Imports = imports, Types = types };
    }

    private static Graph.TypeInfo BuildType(BaseTypeDeclarationSyntax decl)
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
        var properties = new List<string>();
        var fields = new List<string>();
        if (decl is TypeDeclarationSyntax typeDecl)
        {
            foreach (var m in typeDecl.Members.OfType<MethodDeclarationSyntax>())
            {
                methods.Add(BuildMethod(m.Identifier.Text, m.ParameterList.Parameters.Count, BuildSignature(m), m));
            }
            foreach (var c in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
            {
                var sig = $"{c.Identifier.Text}({string.Join(", ", c.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "?"))}) (constructor)";
                methods.Add(BuildMethod(c.Identifier.Text, c.ParameterList.Parameters.Count, sig, c));
            }
            // B5: operators/conversions carry real logic too.
            foreach (var op in typeDecl.Members.OfType<OperatorDeclarationSyntax>())
            {
                methods.Add(BuildMethod($"operator {op.OperatorToken.Text}", op.ParameterList.Parameters.Count,
                    $"{op.ReturnType} operator {op.OperatorToken.Text}(...)", op));
            }
            foreach (var op in typeDecl.Members.OfType<ConversionOperatorDeclarationSyntax>())
            {
                methods.Add(BuildMethod($"operator {op.Type}", op.ParameterList.Parameters.Count,
                    $"operator {op.Type}(...)", op));
            }
            // B5: expression-bodied / accessor-bodied properties & indexers hold invocations
            // and branching. Auto-properties (no executable code) are listed via F2 only.
            foreach (var p in typeDecl.Members.OfType<BasePropertyDeclarationSyntax>())
            {
                var pname = PropertyName(p);
                if (HasExecutableBody(p))
                {
                    methods.Add(BuildMethod(pname, 0, $"{p.Type} {pname} {{ … }}", p));
                }
                properties.Add($"{pname} : {p.Type}");
            }
            foreach (var fd in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            {
                foreach (var v in fd.Declaration.Variables)
                {
                    fields.Add($"{v.Identifier.Text} : {fd.Declaration.Type}");
                }
            }
        }

        return new Graph.TypeInfo
        {
            Name = decl.Identifier.Text,
            Kind = kind,
            Namespace = ns,
            Modifiers = decl.Modifiers.ToString(),
            BaseTypes = decl.BaseList?.Types.Select(t => t.Type.ToString()).ToList() ?? [],
            XmlSummary = GetXmlSummary(decl),
            Methods = methods,
            Properties = properties,
            Fields = fields,
        };
    }

    // B5: top-level statements (Program.cs) declare no type, so their invocations and
    // complexity were invisible. Synthesize a "<top-level>" type with a "<main>" member.
    // Returns null when the file has no top-level statements (the common case).
    private static Graph.TypeInfo? BuildTopLevelType(CompilationUnitSyntax root)
    {
        var globals = root.Members.OfType<GlobalStatementSyntax>().ToList();
        if (globals.Count == 0) { return null; }

        var invocations = globals
            .SelectMany(g => g.DescendantNodes().OfType<InvocationExpressionSyntax>())
            .Select(inv => new Graph.InvocationRef(InvokedName(inv.Expression), inv.ArgumentList.Arguments.Count))
            .Where(r => r.Name.Length > 0)
            .Distinct()
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Arity)
            .ToList();
        return new Graph.TypeInfo
        {
            Name = "<top-level>",
            Kind = "top-level",
            Namespace = "",
            XmlSummary = "Top-level statements (program entry point).",
            Methods =
            [
                new Graph.MethodInfo
                {
                    Name = "<main>",
                    Arity = 0,
                    Signature = "<top-level statements>",
                    Cyclomatic = 1 + globals.Sum(g => ComplexityMetrics.Cyclomatic(g) - 1),
                    Cognitive = globals.Sum(ComplexityMetrics.Cognitive),
                    StartLine = LineOf(globals[0], first: true),
                    EndLine = LineOf(globals[^1], first: false),
                    Invocations = invocations,
                },
            ],
        };
    }

    private static Graph.MethodInfo BuildMethod(string name, int arity, string signature, SyntaxNode decl)
    {
        var pl = (decl as BaseMethodDeclarationSyntax)?.ParameterList;
        var (min, max) = ArityRange(pl, arity);
        // Single tree walk for cyclomatic + cognitive + invocations (previously 3 separate
        // traversals of the same body — see plan.md Phase 3 item 1).
        var (cyclomatic, cognitive, invocations) = ComplexityMetrics.ComputeAll(decl);
        return new()
        {
            Name = name,
            Arity = arity,
            MinArity = min,
            MaxArity = max,
            Signature = signature,
            Modifiers = (decl as MemberDeclarationSyntax)?.Modifiers.ToString() ?? "",
            XmlSummary = GetXmlSummary(decl),
            Cyclomatic = cyclomatic,
            Cognitive = cognitive,
            StartLine = LineOf(decl, first: true),
            EndLine = LineOf(decl, first: false),
            Invocations = invocations,
        };
    }

    /// <summary>Legal call-argument range for a parameter list: min = required params
    /// (no default, not <c>params</c>), max = total, or int.MaxValue when the last is
    /// <c>params</c>. Lets the call graph match optional/params/named-arg calls (B6).</summary>
    private static (int Min, int Max) ArityRange(ParameterListSyntax? pl, int arity)
    {
        if (pl is null) { return (arity, arity); }
        var parameters = pl.Parameters;
        var hasParamsArray = parameters.Count > 0 && parameters[^1].Modifiers.Any(SyntaxKind.ParamsKeyword);
        var required = parameters.Count(p => p.Default is null && !p.Modifiers.Any(SyntaxKind.ParamsKeyword));
        var max = hasParamsArray ? int.MaxValue : parameters.Count;
        return (required, max);
    }

    private static string PropertyName(BasePropertyDeclarationSyntax p) => p switch
    {
        PropertyDeclarationSyntax pd => pd.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        EventDeclarationSyntax ed => ed.Identifier.Text,
        _ => "property",
    };

    /// <summary>True when the member has executable code: an expression body, or any accessor
    /// with a block or expression body. Auto-properties (get; set;) return false.</summary>
    private static bool HasExecutableBody(BasePropertyDeclarationSyntax p)
    {
        if (p is PropertyDeclarationSyntax { ExpressionBody: not null }) { return true; }
        if (p is IndexerDeclarationSyntax { ExpressionBody: not null }) { return true; }
        return p.AccessorList?.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null) ?? false;
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

    private static readonly TimeSpan XmlSummaryRegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly System.Text.RegularExpressions.Regex XmlTag =
        new(@"<[^>]+>", System.Text.RegularExpressions.RegexOptions.Compiled, XmlSummaryRegexTimeout);
    private static readonly System.Text.RegularExpressions.Regex Whitespace =
        new(@"\s+", System.Text.RegularExpressions.RegexOptions.Compiled, XmlSummaryRegexTimeout);

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
        try
        {
            text = XmlTag.Replace(text, " ");
            text = Whitespace.Replace(text, " ").Trim();
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { text = text.Trim(); }
        return text;
    }
}
