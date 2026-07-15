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
                case SwitchExpressionArmSyntax:
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
}
