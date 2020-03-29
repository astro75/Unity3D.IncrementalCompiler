using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MonadLib;
using static System.String;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler
{
    public static partial class CodeGeneration
    {
        static CaseClass GenerateCaseClass(
            RecordAttribute attr, SemanticModel model, TypeDeclarationSyntax cds
        ) {
            var symbolInfo = model.GetDeclaredSymbol(cds);
            if (symbolInfo == null) throw new Exception("Could not find symbol");

            var properties = cds.Members.OfType<PropertyDeclarationSyntax>()
                .Where(prop => prop.Modifiers.HasNot(SyntaxKind.StaticKeyword))
                .Where(prop => prop.AccessorList?.Accessors.Any(ads =>
                    ads.IsKind(SyntaxKind.GetAccessorDeclaration)
                    && ads.Body == null
                    && ads.ExpressionBody == null
                ) ?? false)
                .Select(prop => new FieldOrProp(
                    prop.Type, prop.Identifier, prop.Initializer != null, model
                ));

            var fields =
                cds.Members.OfType<FieldDeclarationSyntax>()
                .Where(field => {
                    var modifiers = field.Modifiers;
                    return modifiers.HasNot(SyntaxKind.StaticKeyword)
                        && modifiers.HasNot(SyntaxKind.ConstKeyword);
                })
                .SelectMany(field => {
                    var decl = field.Declaration;
                    var type = decl.Type;
                    return decl.Variables.Select(varDecl => new FieldOrProp(
                        type, varDecl.Identifier, varDecl.Initializer != null, model
                    ));
                });

            var fieldsAndProps = fields.Concat(properties).ToArray();

            var hasAnyFields = fieldsAndProps.Any();

            var initializedFieldsAndProps = fieldsAndProps.Where(_ => !_.initialized).ToImmutableArray();

            var constructor = createIf(
                attr.GenerateConstructor.generateConstructor() && hasAnyFields,
                () => {
                    var params_ = initializedFieldsAndProps.joinCommaSeparated(f => f.type + " " + f.identifier);
                    var body = initializedFieldsAndProps.Any()
                        ? initializedFieldsAndProps
                            .Select(f => $"this.{f.identifier} = {f.identifier}; // {f.typeInfo.Kind} {f.typeInfo.GetType()}")
                            .Tap(s => Join("\n", s) + "\n")
                        : "";

                    return ParseClassMembers($"public {cds.Identifier}({params_}){{{body}}}");
                }
            );

            IReadOnlyList<MemberDeclarationSyntax> createIf(bool condition, Func<SyntaxList<MemberDeclarationSyntax>> a)
                => condition ? (IReadOnlyList<MemberDeclarationSyntax>) a() : Array.Empty<MemberDeclarationSyntax>();

            var toString = createIf(
                attr.GenerateToString,
                () => {
                    var returnString = fieldsAndProps
                        .joinCommaSeparated(f => f.traversable
                            ? f.identifier.ValueText +
                                ": [\" + Helpers.enumerableToString(" + f.identifier.ValueText + ") + \"]"
                            : f.identifier.ValueText +
                                ": \" + " + f.identifier.ValueText + " + \""
                        );

                    return ParseClassMembers(
                        $"public override string ToString() => \"{cds.Identifier.ValueText}({returnString})\";"
                    );
                });

            var getHashCode = createIf(attr.GenerateGetHashCode, () => {
                if (hasAnyFields)
                {
                    var hashLines = Join("\n", fieldsAndProps.Select(f =>
                    {
                        var type = f.typeInfo;
                        var isValueType = type.IsValueType;
                        var name = f.identifier.ValueText;


                        string ValueTypeHash(ITypeSymbol t) {
                            if (t.IsEnum(out var underlyingType))
                            {
                                switch (underlyingType)
                                {
                                    case SpecialType.System_Int64:
                                    case SpecialType.System_UInt64:
                                        return $"{name}.GetHashCode()";
                                    default:
                                        return $"(int) {name}";
                                }
                            }

                            var sType = t.SpecialType;
                            switch (sType)
                            {
                                case SpecialType.System_Byte:
                                case SpecialType.System_SByte:
                                case SpecialType.System_Int16:
                                case SpecialType.System_Int32: return name;
                                case SpecialType.System_UInt32: return "(int) " + name;
                                default: return name + ".GetHashCode()";
                            }
                        }

                        var fieldHashCode = isValueType
                            ? ValueTypeHash(type)
                            : $"({name} == null ? 0 : {name}.GetHashCode())";
                        return $"hashCode = (hashCode * 397) ^ {(fieldHashCode)}; // {type.SpecialType}";
                    }));
                    return ParseClassMembers(
                        $@"public override int GetHashCode() {{
                            unchecked {{
                                var hashCode = 0;
                                {hashLines}
                                return hashCode;
                            }}
                        }}");
                }
                else
                {
                    return ParseClassMembers(
                        $@"public override int GetHashCode() => {cds.Identifier.ValueText.GetHashCode()};");
                }
            });

            /*
            TODO: generic fields
            EqualityComparer<B>.Default.GetHashCode(valClass);
            EqualityComparer<B>.Default.Equals(valClass, other.valClass);
            */

            var typeName = cds.Identifier.ValueText + cds.TypeParameterList;

            var equals = createIf(attr.GenerateComparer, () => {
                var isStruct = cds.Kind() == SyntaxKind.StructDeclaration;
                if (hasAnyFields)
                {
                    var comparisons = fieldsAndProps.Select(f =>
                    {
                        var type = f.typeInfo;
                        var name = f.identifier.ValueText;
                        var otherName = "other." + name;

                        if (type.IsEnum(out _)) return $"{name} == {otherName}";

                        switch (type.SpecialType)
                        {
                            case SpecialType.System_Byte:
                            case SpecialType.System_SByte:
                            case SpecialType.System_Int16:
                            case SpecialType.System_UInt16:
                            case SpecialType.System_Int32:
                            case SpecialType.System_UInt32:
                            case SpecialType.System_Int64:
                            case SpecialType.System_UInt64: return $"{name} == {otherName}";
                            case SpecialType.System_String: return $"string.Equals({name}, {otherName})";
                            default:
                                return $"{name}.Equals({otherName})";
                        }
                    });
                    var equalsExpr = isStruct ? "left.Equals(right)" : "Equals(left, right)";
                    return ParseClassMembers(
                        $"public bool Equals({typeName} other) {{" +
                        (!isStruct
                            ? "if (ReferenceEquals(null, other)) return false;" +
                              "if (ReferenceEquals(this, other)) return true;"
                            : ""
                        ) +
                        $"return {Join(" && ", comparisons)};" +
                        $"}}" +
                        $"public override bool Equals(object obj) {{" +
                        $"  if (ReferenceEquals(null, obj)) return false;" +
                        (!isStruct ? "if (ReferenceEquals(this, obj)) return true;" : "") +
                        $"  return obj is {typeName} && Equals(({typeName}) obj);" +
                        $"}}" +
                        $"public static bool operator ==({typeName} left, {typeName} right) => {equalsExpr};" +
                        $"public static bool operator !=({typeName} left, {typeName} right) => !{equalsExpr};");
                }
                else
                {
                    return ParseClassMembers(
                        $"public bool Equals({typeName} other) => true;" +
                        $"public override bool Equals(object obj) => obj is {typeName};" +
                        $"public static bool operator ==({typeName} left, {typeName} right) => true;" +
                        $"public static bool operator !=({typeName} left, {typeName} right) => false;");
                }
            });


            var withers = createIf(constructor.Count > 0 && initializedFieldsAndProps.Length >= 1 && !symbolInfo.IsAbstract, () => {
                // pubilc Type withVal1(int val1) => new Type(val2, val2);
                var args = initializedFieldsAndProps.joinCommaSeparated(f => f.identifier.Text);
                return ParseClassMembers(Join("\n", initializedFieldsAndProps.Select(f =>
                    $"public {typeName} with{f.identifierFirstLetterUpper} ({f.type + " " + f.identifier}) => " +
                    $"new {typeName}({args});"
                )));
            });

            var fieldsForCopy = initializedFieldsAndProps.Where(f => {
                if (f.typeInfo is ITypeParameterSymbol tp) {
                    // class Type<A> { A unsupported; }
                    // class Type<A> where A : class { A supported; }
                    return tp.HasReferenceTypeConstraint;
                }
                // class Type { int? unsupported; }
                return !f.typeInfo.IsNullable();
            }).ToImmutableArray();

            var copy = createIf(constructor.Count > 0 && fieldsForCopy.Length >= 1 && !symbolInfo.IsAbstract, () => {
                // pubilc Type copy(int? val1 = null, int? val2 = null) => new Type(val2 ?? this.val1, val2 ?? this.val2);
                var args1 = fieldsForCopy.joinCommaSeparated(f =>
                    f.typeInfo.IsValueType
                        ? $"{f.type}? {f.identifier} = null"
                        : $"{f.type} {f.identifier} = null"
                );
                var args2 = initializedFieldsAndProps.joinCommaSeparated(f =>
                    fieldsForCopy.Contains(f)
                        ? $"{f.identifier}?? this.{f.identifier}"
                        : $"this.{f.identifier}"
                );
                return ParseClassMembers($"public {typeName} copy({args1}) => new {typeName}({args2});");
            });

            var baseList = attr.GenerateComparer
                // : IEquatable<TypeName>
                ? SF.BaseList(
                    SF.SingletonSeparatedList<BaseTypeSyntax>(
                        SF.SimpleBaseType(
                            SF.ParseTypeName($"System.IEquatable<{typeName}>")
                )))
                : Extensions.EmptyBaseList;
            var newMembers =
                constructor.Concat(toString).Concat(getHashCode).Concat(equals).Concat(withers).Concat(copy);

            #region Static apply method


            var companion = Maybe.MZero<TypeDeclarationSyntax>();
            {
                if (attr.GenerateConstructor == GeneratedConstructor.ConstructorAndApply) {
                    if (cds.TypeParameterList == null) {
                        newMembers = newMembers.Concat(GenerateStaticApply(cds, initializedFieldsAndProps));
                    }
                    else {
                        companion = Maybe.Just(GenerateCaseClassCompanion(cds, initializedFieldsAndProps));
                    }
                }
            }

            #endregion

            var caseclass = CreatePartial(cds, newMembers, baseList);
            return new CaseClass(caseclass, companion);
        }

        static TypeDeclarationSyntax GenerateCaseClassCompanion(
            TypeDeclarationSyntax cds, ICollection<FieldOrProp> props
        ) {
            // add and remove partial modifier because rider prints
            // warning if partial keyword is not right before class keyword
            var classModifiers =
                cds.Modifiers
                .RemoveOfKind(SyntaxKind.PartialKeyword)
                .RemoveOfKind(SyntaxKind.ReadOnlyKeyword)
                .Add(SyntaxKind.StaticKeyword)
                .Add(SyntaxKind.PartialKeyword);

            var applyMethod = GenerateStaticApply(cds, props);
            return SF.ClassDeclaration(cds.Identifier)
                    .WithModifiers(classModifiers)
                    .WithMembers(applyMethod);
        }
    }
}
