﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using GenerationAttributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static System.String;
using SF = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace IncrementalCompiler {
  public static partial class CodeGeneration {
    static CaseClass GenerateCaseClass(
      RecordAttribute attr, SemanticModel model, TypeDeclarationSyntax cds, INamedTypeSymbol symbolInfo
    ) {
      var members = symbolInfo.GetMembers();

      var fieldsAndProps = members.SelectMany(member => {
        switch (member) {
          case IFieldSymbol fieldSymbol: {
            if (fieldSymbol.IsConst || fieldSymbol.IsStatic) break;
            // backing fields of properties
            if (fieldSymbol.DeclaringSyntaxReferences.Length == 0) break;
            var untypedSyntax = fieldSymbol.DeclaringSyntaxReferences.Single().GetSyntax();
            var syntax = (VariableDeclaratorSyntax) untypedSyntax;
            return new[] {
              new FieldOrProp(
                fieldSymbol.Type, fieldSymbol.Name, syntax.Initializer != null, model
              )
            };
          }
          case IPropertySymbol propertySymbol: {
            if (propertySymbol.IsStatic) break;
            if (propertySymbol.IsIndexer) break;
            var syntax = (PropertyDeclarationSyntax)
              propertySymbol.DeclaringSyntaxReferences.Single().GetSyntax();
            var hasGetterOrSetter = syntax.AccessorList?.Accessors.Any(ads =>
              ads.Body != null || ads.ExpressionBody != null
            ) ?? false;
            if (hasGetterOrSetter) break;
            if (syntax.ExpressionBody != null) break;
            return new[] {
              new FieldOrProp(
                propertySymbol.Type, propertySymbol.Name, syntax.Initializer != null, model
              )
            };
          }
        }

        return Enumerable.Empty<FieldOrProp>();
      }).ToArray();

      var hasAnyFields = fieldsAndProps.Any();

      var initializedFieldsAndProps = fieldsAndProps.Where(_ => !_.initialized).ToImmutableArray();

      var constructor = createIf(
        attr.GenerateConstructor.HasFlag(ConstructorFlags.Constructor) && hasAnyFields,
        () => {
          var parameters = initializedFieldsAndProps.JoinCommaSeparated(f => f.type + " " + f.identifier);
          var body = initializedFieldsAndProps.Any()
            ? initializedFieldsAndProps
              .Select(f => $"this.{f.identifier} = {f.identifier};")
              .Tap(s => Join("\n", s) + "\n")
            : "";

          return ParseClassMembers($"public {cds.Identifier}({parameters}){{{body}}}");
        }
      );

      IReadOnlyList<MemberDeclarationSyntax> createIf(bool condition, Func<SyntaxList<MemberDeclarationSyntax>> a) {
        return condition ? (IReadOnlyList<MemberDeclarationSyntax>) a() : Array.Empty<MemberDeclarationSyntax>();
      }

      var toString = createIf(
        attr.GenerateToString,
        () => {
          var returnString = fieldsAndProps
            .JoinCommaSeparated(f => f.traversable
              ? f.identifier +
                ": [\" + Helpers.enumerableToString(" + f.identifier + ") + \"]"
              : f.identifier +
                ": \" + " + f.identifier + " + \""
            );

          return ParseClassMembers(
            $"public override string ToString() => \"{cds.Identifier.ValueText}({returnString})\";"
          );
        });

      var getHashCode = createIf(attr.GenerateGetHashCode, () => {
        if (hasAnyFields) {
          var hashLines = Join("\n", fieldsAndProps.Select(f => {
            var type = f.typeInfo;
            var isValueType = type.IsValueType;
            var name = f.identifier;


            string ValueTypeHash(ITypeSymbol t) {
              if (t.IsEnum(out var underlyingType))
                switch (underlyingType) {
                  case SpecialType.System_Int64:
                  case SpecialType.System_UInt64:
                    return $"{name}.GetHashCode()";
                  default:
                    return $"(int) {name}";
                }

              var sType = t.SpecialType;
              switch (sType) {
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
            return $"hashCode = (hashCode * 397) ^ {fieldHashCode}; // {type.SpecialType}";
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

        return ParseClassMembers(
          $@"public override int GetHashCode() => {cds.Identifier.ValueText.GetHashCode()};");
      });

      /*
      TODO: generic fields
      EqualityComparer<B>.Default.GetHashCode(valClass);
      EqualityComparer<B>.Default.Equals(valClass, other.valClass);
      */

      var typeName = cds.Identifier.ValueText + cds.TypeParameterList;

      var equals = createIf(attr.GenerateComparer, () => {
        var isStruct = cds.Kind() == SyntaxKind.StructDeclaration;
        if (hasAnyFields) {
          var comparisons = fieldsAndProps.Select(f => {
            var type = f.typeInfo;
            var name = f.identifier;
            var otherName = "other." + name;

            if (type.IsEnum(out _)) return $"{name} == {otherName}";

            switch (type.SpecialType) {
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
                return createEquals(isStruct: type.TypeKind == TypeKind.Struct, name, otherName);
            }
          });

          var equalsExpr = createEquals(isStruct, "left", "right");
          return ParseClassMembers(
            $"public bool Equals({typeName} other) {{" +
            (!isStruct
              ? "if (ReferenceEquals(null, other)) return false;" +
                "if (ReferenceEquals(this, other)) return true;"
              : ""
            ) +
            $"return {Join(" && ", comparisons)};" +
            "}" +
            "public override bool Equals(object obj) {" +
            "  if (ReferenceEquals(null, obj)) return false;" +
            (!isStruct ? "if (ReferenceEquals(this, obj)) return true;" : "") +
            $"  return obj is {typeName} && Equals(({typeName}) obj);" +
            "}" +
            $"public static bool operator ==({typeName} left, {typeName} right) => {equalsExpr};" +
            $"public static bool operator !=({typeName} left, {typeName} right) => !{equalsExpr};");


          static string createEquals(bool isStruct, string firstName, string secondName) =>
            isStruct ? $"{firstName}.Equals({secondName})" : $"System.Object.Equals({firstName}, {secondName})";
        }

        return ParseClassMembers(
          $"public bool Equals({typeName} other) => true;" +
          $"public override bool Equals(object obj) => obj is {typeName};" +
          $"public static bool operator ==({typeName} left, {typeName} right) => true;" +
          $"public static bool operator !=({typeName} left, {typeName} right) => false;");
      });


      var withers = createIf(
        attr.GenerateConstructor.HasFlag(ConstructorFlags.Withers) && constructor.Count > 0 &&
        initializedFieldsAndProps.Length >= 1 && !symbolInfo.IsAbstract,
        () => {
          // pubilc Type withVal1(int val1) => new Type(val2, val2);
          var args = initializedFieldsAndProps.JoinCommaSeparated(f => f.identifier);
          return ParseClassMembers(Join("\n", initializedFieldsAndProps.Select(f =>
            $"public {typeName} with{f.identifierFirstLetterUpper} ({f.type + " " + f.identifier}) => " +
            $"new {typeName}({args});"
          )));
        }
      );

      var fieldsForCopy = initializedFieldsAndProps.Where(f => {
        if (f.typeInfo is ITypeParameterSymbol tp) // class Type<A> { A unsupported; }
          // class Type<A> where A : class { A supported; }
          return tp.HasReferenceTypeConstraint;
        // class Type { int? unsupported; }
        return !f.typeInfo.IsNullable();
      }).ToImmutableArray();

      var copy = createIf(
        attr.GenerateConstructor.HasFlag(ConstructorFlags.Copy) && constructor.Count > 0 &&
        fieldsForCopy.Length >= 1 && !symbolInfo.IsAbstract,
        () => {
          // pubilc Type copy(int? val1 = null, int? val2 = null) => new Type(val2 ?? this.val1, val2 ?? this.val2);
          var args1 = fieldsForCopy.JoinCommaSeparated(f =>
            f.typeInfo.IsValueType
              ? $"{f.type}? {f.identifier} = null"
              : $"{f.type} {f.identifier} = null"
          );
          var args2 = initializedFieldsAndProps.JoinCommaSeparated(f =>
            fieldsForCopy.Contains(f)
              ? $"{f.identifier}?? this.{f.identifier}"
              : $"this.{f.identifier}"
          );
          return ParseClassMembers($"public {typeName} copy({args1}) => new {typeName}({args2});");
        }
      );

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

      TypeDeclarationSyntax? companion = null;
      {
        if (attr.GenerateConstructor.HasFlag(ConstructorFlags.Apply)) {
          if (cds.TypeParameterList == null)
            newMembers = newMembers.Concat(GenerateStaticApply(cds, initializedFieldsAndProps));
          else
            companion = GenerateCaseClassCompanion(cds, initializedFieldsAndProps);
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

      // Keywords that we like to keep: public, internal, ... maybe more
      var classModifiers =
        cds.Modifiers
          .RemoveOfKind(SyntaxKind.PartialKeyword)
          .RemoveOfKind(SyntaxKind.ReadOnlyKeyword)
          .RemoveOfKind(SyntaxKind.SealedKeyword)
          .Add(SyntaxKind.StaticKeyword)
          .Add(SyntaxKind.PartialKeyword);

      var applyMethod = GenerateStaticApply(cds, props);
      return SF.ClassDeclaration(cds.Identifier)
        .WithModifiers(classModifiers)
        .WithMembers(applyMethod);
    }
  }

  internal static class ConstructorFlagsExts {
    public static bool HasFlag(this ConstructorFlags flags, ConstructorFlags flag) {
      return (flags & flag) == flag;
    }
  }
}
