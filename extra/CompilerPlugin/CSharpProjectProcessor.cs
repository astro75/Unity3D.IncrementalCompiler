using System;
using System.Collections.Generic;
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

        if (xdoc.Root == null)
        {
            Debug.LogError("Project file root not found");
            return content;
        }

        var ns = xdoc.Root.GetDefaultNamespace();

        {
            // SetUpCorrectLangVersion
            const string csharpVersion = "8";
            xdoc.Descendants(ns + "LangVersion").Remove();
            var propertyGroupElement = xdoc.Descendants(ns + "PropertyGroup").First();
            propertyGroupElement.Add(new XElement(ns + "LangVersion", csharpVersion));
        }

        {
            // EnableUnsafeCode
            xdoc.Descendants(ns + "AllowUnsafeBlocks").Remove();
            var propertyGroup = xdoc.Descendants(ns + "PropertyGroup").First();
            propertyGroup.Add(new XElement(ns + "AllowUnsafeBlocks", "True"));
        }

        {
            // RemoveAnnoyingReferences
            xdoc.Descendants(ns + "Reference").Where(element =>
            {
                var include = element.Attribute("Include")?.Value;
                return include == "Boo.Lang" || include == "UnityScript.Lang";
            }).Remove();
        }

        var compileName = ns + "Compile";
        var noneName = ns + "None";
        var includeName = (XName) "Include";
        var linkName = (XName) "Link";

        var allCsPaths = xdoc.Descendants(compileName)
            .Select(element => element.Attribute(includeName)?.Value)
            .OfType<string>()
            .ToArray();

        var commonPrefix = allCsPaths.Length > 1 ? FindCommonPrefix(allCsPaths) : "";

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
                {
                    var partialsFolder = Path.Combine(SharedData.GeneratedFolder, assemblyName, "partials");
                    var files = GetCsFiles(partialsFolder).ToArray();
                    var newFiles = files.Select(
                        file => (object) new XElement(
                            compileName,
                            new XAttribute(includeName, file),
                            new XAttribute(linkName, convertLink(file))
                        )
                    );
                    xdoc.Root.Add(new XElement(ns + "ItemGroup", newFiles.ToArray()));
                }
                {
                    var macrosFolder = Path.Combine(SharedData.GeneratedFolder, assemblyName, "macros");
                    var files = GetCsFiles(macrosFolder).ToArray();
                    var newFiles = files.Select(
                        file => (object) new XElement(
                            noneName,
                            new XAttribute(includeName, file),
                            new XAttribute(linkName, convertLink(file))
                        )
                    );
                    xdoc.Root.Add(new XElement(ns + "ItemGroup", newFiles.ToArray()));
                }

                string convertLink(string file) {
                    var assetsIdx = file.IndexOf("Assets", StringComparison.Ordinal);
                    if (assetsIdx != -1)
                    {
                        file = file.Substring(assetsIdx);
                    }
                    return EnsureDoesNotStartWith(file, commonPrefix);
                }

            }

            RemoveOldGeneratedFiles();
            AddGeneratedFiles();
        }

        var writer = new Utf8StringWriter();
        xdoc.Save(writer);
        return writer.ToString();
    }

    static IEnumerable<string> GetCsFiles(string path) =>
        GetFiles(path).Where(_ => _.EndsWith(".cs", StringComparison.Ordinal));

    static IEnumerable<string> GetFiles(string path) {
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0) {
            path = queue.Dequeue();
            try {
                foreach (string subDir in Directory.GetDirectories(path)) {
                    queue.Enqueue(subDir);
                }
            }
            catch(Exception ex) {
                Console.Error.WriteLine(ex);
            }
            string[]? files = null;
            try {
                files = Directory.GetFiles(path);
            }
            catch (Exception ex) {
                Console.Error.WriteLine(ex);
            }
            if (files != null) {
                for (int i = 0; i < files.Length; i++) {
                    yield return files[i];
                }
            }
        }
    }

    static string FindCommonPrefix(string[] paths) {
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
}
