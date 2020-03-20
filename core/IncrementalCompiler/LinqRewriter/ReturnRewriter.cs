using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Shaman.Roslyn.LinqRewrite
{
    public class ReturnRewriter : CSharpSyntaxRewriter
    {
        // ExpressionSyntax variable;
        readonly string gotoName;
        readonly string tempVarName;

        public bool addedGoto { get; private set; }

        public ReturnRewriter(string gotoName, string tempVarName) {
            // this.variable = variable;
            this.gotoName = gotoName;
            this.tempVarName = tempVarName;
        }

        public override SyntaxNode VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;
        public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;
        public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

        public override SyntaxNode VisitReturnStatement(ReturnStatementSyntax node) {
            addedGoto = true;
            if (node.Expression == null)
            {
                return SF.GotoStatement(SyntaxKind.GotoStatement, SF.IdentifierName(gotoName));
            }
            return SF.Block(
                SF.ExpressionStatement(SF.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    SF.IdentifierName(tempVarName),
                    node.Expression)),
                SF.GotoStatement(SyntaxKind.GotoStatement, SF.IdentifierName(gotoName))
            );
        }
    }
}
