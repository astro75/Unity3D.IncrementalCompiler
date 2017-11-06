using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler
{
    internal static class Extensions
    {
        public static bool Has(this BasePropertyDeclarationSyntax decl, SyntaxKind kind)
            => decl.Modifiers.Has(kind);

        public static bool Has(this BaseMethodDeclarationSyntax decl, SyntaxKind kind)
            => decl.Modifiers.Has(kind);

        public static bool Has(this SyntaxTokenList tokens, SyntaxKind kind)
            => tokens.Any(m => m.IsKind(kind));

        public static bool HasNot(this BasePropertyDeclarationSyntax decl, SyntaxKind kind)
            => decl.Modifiers.HasNot(kind);

        public static bool HasNot(this BaseMethodDeclarationSyntax decl, SyntaxKind kind)
            => decl.Modifiers.HasNot(kind);

        public static bool HasNot(this SyntaxTokenList tokens, SyntaxKind kind)
            => tokens.All(m => !m.IsKind(kind));

        public static PropertyDeclarationSyntax Remove(this PropertyDeclarationSyntax decl, SyntaxKind kind)
            => decl.WithModifiers(decl.Modifiers.Remove(kind));

        public static MethodDeclarationSyntax Remove(this MethodDeclarationSyntax decl, SyntaxKind kind)
            => decl.WithModifiers(decl.Modifiers.Remove(kind));

        public static SyntaxTokenList Remove(this SyntaxTokenList tokens, SyntaxKind kind)
            => tokens.Remove(SF.Token(kind));

        public static SyntaxTokenList Add(this SyntaxTokenList modifiers, SyntaxKind kind)
            => modifiers.Any(m => m.IsKind(kind)) ? modifiers : modifiers.Add(SF.Token(kind));

        public static SyntaxTokenList Modifiers(this MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case FieldDeclarationSyntax s1:
                    return s1.Modifiers;
                case PropertyDeclarationSyntax s2:
                    return s2.Modifiers;
                case MethodDeclarationSyntax s:
                    return s.Modifiers;
            }
            return SF.TokenList();
        }

        public static MemberDeclarationSyntax WithModifiers(this MemberDeclarationSyntax member, SyntaxTokenList modifiers)
        {
            switch (member)
            {
                case FieldDeclarationSyntax s1:
                    return s1.WithModifiers(modifiers);
                case PropertyDeclarationSyntax s2:
                    return s2.WithModifiers(modifiers);
                case MethodDeclarationSyntax s:
                    return s.WithModifiers(modifiers);
            }
            return member;
        }

        public static LiteralExpressionSyntax StringLiteral(this string value)
            => SF.LiteralExpression(SyntaxKind.StringLiteralExpression, SF.Literal(value));
    }
}
