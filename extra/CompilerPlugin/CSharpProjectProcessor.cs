using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

// This script modifies .csproj files:
// 1) enables unsafe code,
// 2) removes C# 4.0 version restriction introduced by Visual Studio Tools for Unity.

[InitializeOnLoad]
public class CSharpProjectPostprocessor : AssetPostprocessor
{
    // We need to run this after Rider postprocessor, because we override LangVersion
    // Rider postprocessor has order of 10
    public override int GetPostprocessOrder() => 11;

    // In case VSTU is installed
    static CSharpProjectPostprocessor() {
        if (unityVersion >= new Version(2018, 1)) return;
        OnGeneratedCSProjectFiles();
    }

    public static Version unityVersion => new Version(Application.unityVersion.Split(".".ToCharArray()).Take(2).Aggregate((a, b) => a + "." + b));

    // In case VSTU is not installed
    private static void OnGeneratedCSProjectFiles()
    {
        Debug.Log("Incremental compiler: " + nameof(OnGeneratedCSProjectFiles));

        foreach (string projectFile in Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj"))
        {
            string content = File.ReadAllText(projectFile);
            content = ModifyProjectFile(content);
            File.WriteAllText(projectFile, content);
        }
    }

    [UsedImplicitly]
    public static string OnGeneratedCSProject(string path, string contents)
    {
        try
        {
            return ModifyProjectFile(contents);
        }
        catch (Exception ex)
        {
            Debug.LogError((object) ex);
            return contents;
        }
    }

    private static string ModifyProjectFile(string content)
    {
        var xdoc = XDocument.Parse(content);

        SetUpCorrectLangVersion(xdoc);
        EnableUnsafeCode(xdoc);
        RemoveAnnoyingReferences(xdoc);

        AddDefine(xdoc, CustomCSharpCompiler.COMPILER_DEFINE);
        {
            var ns = xdoc.Root.GetDefaultNamespace();
            var compileName = ns + "Compile";
            var includeName = (XName) "Include";
            const string GENERATED = "Generated";
            var generatedPathStart = GENERATED + Path.DirectorySeparatorChar;

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
                var filename = Path.Combine(GENERATED, $"Generated-files-{assemblyName}.txt");
                if (!File.Exists(filename)) return;
                var filesToAdd = File.ReadAllLines(filename);
                var newFiles = filesToAdd.Select(
                    file => (object)new XElement(compileName, new XAttribute(includeName, file))
                );
                xdoc.Root.Add(new XElement(ns + "ItemGroup", newFiles.ToArray()));
            }

            RemoveOldGeneratedFiles();
            AddGeneratedFiles();
        }

        var writer = new Utf8StringWriter();
        xdoc.Save(writer);
        return writer.ToString();
    }



    static string EnsureDoesNotEndWith(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal)
        ? s.Substring(s.Length - suffix.Length)
        : s;

    private class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private static void SetUpCorrectLangVersion(XDocument xdoc)
    {
        var csharpVersion = "7.3";
        /*
        if (Directory.Exists("CSharp70Support"))
        {
            csharpVersion = "7";
        }
        else if (Directory.Exists("CSharp60Support"))
        {
            csharpVersion = "6";
        }
        else
        {
            csharpVersion = "default";
        }*/

        var ns = xdoc.Root.GetDefaultNamespace();
        xdoc.Descendants(ns + "LangVersion").Remove();
        var propertyGroupElement = xdoc.Descendants(ns + "PropertyGroup").First();
        propertyGroupElement.Add(new XElement(ns + "LangVersion", csharpVersion));
    }

    private static void EnableUnsafeCode(XDocument xdoc)
    {
        var ns = xdoc.Root.GetDefaultNamespace();
        xdoc.Descendants(ns + "AllowUnsafeBlocks").Remove();
        var propertyGroup = xdoc.Descendants(ns + "PropertyGroup").First();
        propertyGroup.Add(new XElement(ns + "AllowUnsafeBlocks", "True"));
    }

    private static void RemoveAnnoyingReferences(XDocument xdoc)
    {
        var ns = xdoc.Root.GetDefaultNamespace();

        (from element in xdoc.Descendants(ns + "Reference")
         let include = element.Attribute("Include").Value
         where include == "Boo.Lang" || include == "UnityScript.Lang"
         select element).Remove();
    }

    private static void AddDefine(XDocument xdoc, string define)
    {
        var ns = xdoc.Root.GetDefaultNamespace();
        foreach (var defines in xdoc.Descendants(ns + "DefineConstants"))
        {
            defines.Value += ";" + define;
        }
    }
}
