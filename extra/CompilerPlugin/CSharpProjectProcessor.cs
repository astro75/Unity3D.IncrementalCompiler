using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using IncrementalCompiler;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

// This script modifies .csproj files:
// 1) enables unsafe code,
// 2) removes C# 4.0 version restriction introduced by Visual Studio Tools for Unity.

public class CSharpProjectPostprocessor : AssetPostprocessor
{
    [UsedImplicitly]
    public static string OnGeneratedCSProject(string path, string contents)
    {
        try
        {
            return ModifyProjectFile(contents);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
            return contents;
        }
    }

    static string ModifyProjectFile(string content)
    {
        var xdoc = XDocument.Parse(content);

        SetUpCorrectLangVersion(xdoc);
        EnableUnsafeCode(xdoc);
        RemoveAnnoyingReferences(xdoc);

        AddDefine(xdoc, CustomCSharpCompiler.COMPILER_DEFINE);

        if (xdoc.Root == null)
        {
            Debug.LogError("Project file root not found");
            return content;
        }

        var ns = xdoc.Root.GetDefaultNamespace();

        var compileName = ns + "Compile";
        var noneName = ns + "None";
        var includeName = (XName) "Include";
        var linkName = (XName) "Link";

        var allCsPaths = xdoc.Descendants(compileName)
            .Select(element => element.Attribute(includeName)?.Value)
            .OfType<string>()
            .ToArray();

        var commonPrefix = findCommonPrefix(allCsPaths);

        {
            if (commonPrefix.Length > 0)
            {
                foreach (var fileElement in xdoc.Descendants(compileName).Concat(xdoc.Descendants(noneName)))
                {
                    var includeAttribute = fileElement.Attribute(includeName);
                    if (includeAttribute != null)
                    {
                        fileElement.SetAttributeValue(
                            linkName,
                            EnsureDoesNotStartWith(includeAttribute.Value, commonPrefix));
                    }
                }
            }
        }

        {
            var generatedPathStart = SharedData.GeneratedFolder + Path.DirectorySeparatorChar;

            void RemoveOldGeneratedFiles() {
                var toRemove = xdoc.Root.Descendants(compileName).Where(fileNode =>
                    fileNode.Attribute(includeName)?.Value
                        .StartsWith(generatedPathStart, StringComparison.Ordinal) ?? false
                ).ToArray();
                foreach (var element in toRemove) element.Remove();
            }

            void AddGeneratedFiles()
            {
                var assemblyName = xdoc.Root.Descendants(ns + "AssemblyName").Select(el => el.Value).First();
                // .dll suffix appears here if we select VS2017 in unity preferences
                assemblyName = EnsureDoesNotEndWith(assemblyName, ".dll");
                var filename = Path.Combine(SharedData.GeneratedFolder, SharedData.GeneratedFilesListTxt(assemblyName));
                if (!File.Exists(filename)) return;
                var filesToAdd = File.ReadAllLines(filename);
                var newFiles = filesToAdd.Select(
                    file => (object)new XElement(
                        compileName,
                        new XAttribute(includeName, file),
                        new XAttribute(linkName, convertLink(file))
                    )
                );

                string convertLink(string file) {
                    file = EnsureDoesNotEndWith(file, ".cs") + ".partials.cs";
                    var assetsIdx = file.IndexOf("Assets", StringComparison.Ordinal);
                    if (assetsIdx != -1)
                    {
                        file = file.Substring(assetsIdx);
                    }
                    return EnsureDoesNotStartWith(file, commonPrefix);
                }

                xdoc.Root.Add(new XElement(ns + "ItemGroup", newFiles.ToArray()));
            }

            RemoveOldGeneratedFiles();
            AddGeneratedFiles();
        }

        var writer = new Utf8StringWriter();
        xdoc.Save(writer);
        return writer.ToString();
    }

    static string findCommonPrefix(string[] paths) {
        if (paths.Length == 0) return "";
        var result = paths[0];
        foreach (var path in paths)
        {
            while (result.Length > 0 && !path.StartsWith(result, StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 1);
            }
        }
        return result;
    }

    static string EnsureDoesNotEndWith(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s.Substring(0, s.Length - suffix.Length) : s;

    static string EnsureDoesNotStartWith(string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.Ordinal) ? s.Substring(prefix.Length) : s;

    class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    static void SetUpCorrectLangVersion(XDocument xdoc)
    {
        const string csharpVersion = "8";
        var ns = xdoc.Root.GetDefaultNamespace();
        xdoc.Descendants(ns + "LangVersion").Remove();
        var propertyGroupElement = xdoc.Descendants(ns + "PropertyGroup").First();
        propertyGroupElement.Add(new XElement(ns + "LangVersion", csharpVersion));
    }

    static void EnableUnsafeCode(XDocument xdoc)
    {
        var ns = xdoc.Root.GetDefaultNamespace();
        xdoc.Descendants(ns + "AllowUnsafeBlocks").Remove();
        var propertyGroup = xdoc.Descendants(ns + "PropertyGroup").First();
        propertyGroup.Add(new XElement(ns + "AllowUnsafeBlocks", "True"));
    }

    static void RemoveAnnoyingReferences(XDocument xdoc)
    {
        var ns = xdoc.Root.GetDefaultNamespace();
        xdoc.Descendants(ns + "Reference").Where(element =>
        {
            var include = element.Attribute("Include")?.Value;
            return include == "Boo.Lang" || include == "UnityScript.Lang";
        }).Remove();
    }

    static void AddDefine(XDocument xdoc, string define)
    {
        var ns = xdoc.Root.GetDefaultNamespace();
        foreach (var defines in xdoc.Descendants(ns + "DefineConstants"))
        {
            defines.Value += ";" + define;
        }
    }
}
