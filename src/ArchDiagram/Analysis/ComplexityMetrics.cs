// Syntax-only complexity metrics (no semantic model), matching the analyzer's
// design in CSharpSyntaxAnalyzer. Both metrics operate on the declaration node so
// they see the full method/constructor body.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchDiagram.Analysis;

/// <summary>Cyclomatic complexity (independent paths) and SonarSource cognitive
/// complexity (how tangled the control flow is) for a single method/constructor
/// declaration. Kept as small handlers so the file itself stays low-complexity.
///
/// Note: this is a pragmatic subset of the SonarSource cognitive-complexity spec.
/// It applies the nesting penalty to control-flow structures and a flat increment
/// to else clauses and boolean-operator tokens, but does NOT coalesce runs of
/// mixed &amp;&amp;/|| operators (Sonar counts alternations). The displayed severity
/// bands are wide enough that this approximation does not change a method's level.</summary>
internal static class ComplexityMetrics
{
    /// <summary>1 + one per branch-producing node. Each &amp;&amp;/||/?? operator adds a path.</summary>
    internal static int Cyclomatic(SyntaxNode body)
    {
        var count = 1;
        foreach (var node in body.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                // The `_ =>` default arm is not a branch, matching how the switch-*statement*
                // `default:` (DefaultSwitchLabelSyntax) is already excluded above.
                case SwitchExpressionArmSyntax arm when arm.Pattern is not DiscardPatternSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:
                    count++;
                    break;
                case BinaryExpressionSyntax b when IsShortCircuitOrCoalesce(b.OperatorToken):
                    count++;
                    break;
            }
        }
        return count;
    }

    private static bool IsShortCircuitOrCoalesce(SyntaxToken op) =>
        op.IsKind(SyntaxKind.AmpersandAmpersandToken)
        || op.IsKind(SyntaxKind.BarBarToken)
        || op.IsKind(SyntaxKind.QuestionQuestionToken);

    /// <summary>SonarSource cognitive complexity: a structural increment for each
    /// break in linear flow, plus a nesting penalty for the nesting-inducing
    /// structures. else/else-if add a flat +1 with no nesting penalty.</summary>
    internal static int Cognitive(SyntaxNode body)
    {
        var walker = new CognitiveWalker();
        walker.Visit(body);
        return walker.Score;
    }

    private sealed class CognitiveWalker : CSharpSyntaxWalker
    {
        public int Score { get; private set; }
        private int _nesting;

        // Structures that add (1 + nesting) AND increase nesting for their body.
        private void Nested(SyntaxNode node)
        {
            Score += 1 + _nesting;
            _nesting++;
            base.DefaultVisit(node);
            _nesting--;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // The `if` itself is nested-scored; a chained `else`/`else if` is a
            // flat +1 handled via VisitElseClause below.
            Score += 1 + _nesting;
            _nesting++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nesting--;
            if (node.Else is not null) { Visit(node.Else); }
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            Score += 1; // flat +1, no nesting penalty (Sonar rule)
            Visit(node.Statement);
        }

        public override void VisitForStatement(ForStatementSyntax node) => Nested(node);
        public override void VisitForEachStatement(ForEachStatementSyntax node) => Nested(node);
        public override void VisitWhileStatement(WhileStatementSyntax node) => Nested(node);
        public override void VisitDoStatement(DoStatementSyntax node) => Nested(node);
        public override void VisitSwitchStatement(SwitchStatementSyntax node) => Nested(node);
        public override void VisitCatchClause(CatchClauseSyntax node) => Nested(node);
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) => Nested(node);

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken)
             || node.OperatorToken.IsKind(SyntaxKind.BarBarToken))
            {
                Score += 1; // flat +1, no nesting change
            }
            base.VisitBinaryExpression(node);
        }
    }

    /// <summary>Cyclomatic + cognitive complexity + invocation collection in a single tree
    /// walk, for the hot path (one call per method/constructor across the whole codebase —
    /// see CSharpSyntaxAnalyzer.BuildMethod). Produces exactly the same numbers as calling
    /// <see cref="Cyclomatic"/>, <see cref="Cognitive"/> and the analyzer's invocation
    /// collector separately, verified by ComplexityMetricsTests. The rare top-level
    /// ("&lt;main&gt;") path still calls the three original methods independently — it runs
    /// once per file, not once per method, so the duplication there doesn't matter.</summary>
    internal static (int Cyclomatic, int Cognitive, List<Graph.InvocationRef> Invocations) ComputeAll(SyntaxNode body)
    {
        var walker = new CombinedWalker();
        walker.Visit(body);
        var invocations = walker.Invocations
            .Distinct()
            .OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Arity)
            .ToList();
        return (walker.Cyclomatic, walker.Cognitive, invocations);
    }

    private static string InvokedName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax ma => InvokedName(ma.Name),
        GenericNameSyntax g => g.Identifier.Text,
        MemberBindingExpressionSyntax mb => InvokedName(mb.Name),
        _ => "",
    };

    /// <summary>Combines the cyclomatic switch (over every descendant, matched by kind) and
    /// the cognitive nesting walker (recursive, only over control-flow-affecting kinds) into
    /// one traversal, plus invocation collection. Mapping from the two originals:
    /// - Nodes cyclomatic counts but cognitive doesn't score individually (case labels, switch
    ///   expression arms) fall through to <see cref="DefaultVisit"/>, which does the cyclomatic-only increment.
    /// - Nodes both score (if/loops/switch-statement/catch/conditional) increment cyclomatic
    ///   inline in the same override that does the cognitive nesting.
    /// - Binary operators: &amp;&amp;/|| count for both; ?? counts for cyclomatic only (matches
    ///   the two originals, which disagree on ?? by design — see the class doc comment).
    /// - <c>SwitchStatementSyntax</c> itself is cognitive-nested but NOT a cyclomatic node
    ///   (only its case labels are), matching <see cref="Cyclomatic"/> exactly.</summary>
    private sealed class CombinedWalker : CSharpSyntaxWalker
    {
        public int Cyclomatic { get; private set; } = 1;
        public int Cognitive { get; private set; }
        public List<Graph.InvocationRef> Invocations { get; } = [];
        private int _nesting;

        private void Nested(SyntaxNode node, bool cyclomaticToo)
        {
            if (cyclomaticToo) { Cyclomatic++; }
            Cognitive += 1 + _nesting;
            _nesting++;
            base.DefaultVisit(node);
            _nesting--;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            Cyclomatic++;
            Cognitive += 1 + _nesting;
            _nesting++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nesting--;
            if (node.Else is not null) { Visit(node.Else); }
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            Cognitive += 1; // flat +1, no nesting penalty (Sonar rule)
            Visit(node.Statement);
        }

        public override void VisitForStatement(ForStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitForEachStatement(ForEachStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitWhileStatement(WhileStatementSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitDoStatement(DoStatementSyntax node) => Nested(node, cyclomaticToo: true);
        // The switch STATEMENT itself is not a cyclomatic node — only its case labels are
        // (handled in DefaultVisit below), matching Cyclomatic() exactly.
        public override void VisitSwitchStatement(SwitchStatementSyntax node) => Nested(node, cyclomaticToo: false);
        public override void VisitCatchClause(CatchClauseSyntax node) => Nested(node, cyclomaticToo: true);
        public override void VisitConditionalExpression(ConditionalExpressionSyntax node) => Nested(node, cyclomaticToo: true);

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            if (node.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken)
             || node.OperatorToken.IsKind(SyntaxKind.BarBarToken))
            {
                Cyclomatic++;
                Cognitive += 1; // flat +1, no nesting change
            }
            else if (node.OperatorToken.IsKind(SyntaxKind.QuestionQuestionToken))
            {
                Cyclomatic++; // cyclomatic-only, matching Cyclomatic()'s IsShortCircuitOrCoalesce
            }
            base.VisitBinaryExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var name = InvokedName(node.Expression);
            if (name.Length > 0) { Invocations.Add(new Graph.InvocationRef(name, node.ArgumentList.Arguments.Count)); }
            base.VisitInvocationExpression(node);
        }

        public override void DefaultVisit(SyntaxNode node)
        {
            switch (node)
            {
                case CaseSwitchLabelSyntax:
                case CasePatternSwitchLabelSyntax:
                // The `_ =>` default arm is not a branch, matching Cyclomatic().
                case SwitchExpressionArmSyntax arm when arm.Pattern is not DiscardPatternSyntax:
                    Cyclomatic++;
                    break;
            }
            base.DefaultVisit(node);
        }
    }
}
