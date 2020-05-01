using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using IncrementalCompiler;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

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
            // import .targets file
            var assemblyName = xdoc.Root.Descendants(ns + "AssemblyName").Select(el => el.Value).First();
            // .dll suffix appears here if we select VS2017 in unity preferences
            assemblyName = EnsureDoesNotEndWith(assemblyName, ".dll");

            var targetsPath = Path.Combine(SharedData.GeneratedFolder, assemblyName + ".targets");
            if (File.Exists(targetsPath))
            {
                xdoc.Root.Add(new XElement(ns + "Import", new XAttribute("Project", targetsPath)));
            }
        }

        var writer = new Utf8StringWriter();
        xdoc.Save(writer);
        return writer.ToString();
    }

    static string EnsureDoesNotEndWith(string s, string suffix) =>
        s.EndsWith(suffix, StringComparison.Ordinal) ? s.Substring(0, s.Length - suffix.Length) : s;

    class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
