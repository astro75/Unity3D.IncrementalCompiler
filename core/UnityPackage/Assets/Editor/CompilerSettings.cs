﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Diagnostics;
 using System.Globalization;
 using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Debug = UnityEngine.Debug;

public class CompilerSettings : EditorWindow
{
    public enum CompilerType
    {
        Auto,
        Mono3,
        Mono5,
        Mono6,
        Microsoft6,
        Incremental6,
    }

    public struct UniversalCompilerSettings
    {
        public CompilerType Compiler;
    }

    public enum PrebuiltOutputReuseType
    {
        None,
        WhenNoChange,
        WhenNoSourceChange
    }

    public struct IncrementalCompilerSettings
    {
        public PrebuiltOutputReuseType PrebuiltOutputReuse;
    }

    struct LogElement {
        public readonly string target;
        public readonly long durationMs;
        public readonly DateTime time;

        public LogElement(string target, long durationMs, DateTime time) {
            this.target = target;
            this.durationMs = durationMs;
            this.time = time;
        }
    }

    const string UcsFilePath = "./Compiler/UniversalCompiler.xml";
    const string UcLogFilePath = "./Compiler/Temp/UniversalCompiler.log";
    const string IcsFilePath = "./Compiler/IncrementalCompiler.xml";

    DateTime _ucsLastWriteTime;
    UniversalCompilerSettings _ucs;
    string _ucVersion;
    LogElement[] _ucLastBuildLog = new LogElement[0];
    DateTime _icsLastWriteTime;
    DateTime _ucLogLastWriteTime;
    IncrementalCompilerSettings _ics;
    Process _icProcess;

    [MenuItem("Assets/Open C# Compiler Settings...")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow(typeof(CompilerSettings));
    }

    public void OnDisable()
    {
        // When unity3d builds projec reloads built assemblies, build logs should be updated.
        // OnDisable is called just after starting building and it can make unity3d redraw this window.
        // http://answers.unity3d.com/questions/704066/callback-before-unity-reloads-editor-assemblies.html
        Repaint();
    }

    public void OnGUI()
    {
        OnGUI_Compiler();
        OnGUI_IncrementalCompilerSettings();
        OnGUI_IncrementalCompilerStatus();
        OnGUI_BuildTime();
    }

    void OnGUI_Compiler()
    {
        GUILayout.Label("Compiler", EditorStyles.boldLabel);

        // LoadUniversalCompilerSettings();
        // UniversalCompilerSettings ucs;
        // ucs.Compiler = (CompilerType)EditorGUILayout.EnumPopup("Compiler:", _ucs.Compiler);
        // if (ucs.Equals(_ucs) == false)
        // {
        //     _ucs = ucs;
        //     SaveUniversalCompilerSettings();
        // }

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Version", GetUniversalCompilerVersion());
        EditorGUI.BeginDisabledGroup(!File.Exists(UcLogFilePath));
        if (GUILayout.Button("Log"))
            ShowUniversalCompilerClientLog();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    void OnGUI_BuildTime()
    {
        var durations = GetUniversalCompilerLastBuildLogs();
        GUILayout.Label("Last Build Times");
        EditorGUI.indentLevel += 1;
        var now = DateTime.UtcNow;
        for (var index = 0; index < durations.Length; index++) {
            var duration = durations[index];
            if (index > 0) {
                var prevDuration = durations[index - 1];
                if ((prevDuration.time - duration.time).TotalSeconds > 15) {
                    // insert space between different compilation runs
                    EditorGUILayout.LabelField("---");
                }
            }
            var ago = now - duration.time;
            var seconds = (int) ago.TotalSeconds;
            EditorGUILayout.LabelField($"{seconds,4} s ago, {duration.durationMs:N0} ms, {duration.target}");
        }

        EditorGUI.indentLevel -= 1;
    }

    void LoadUniversalCompilerSettings()
    {
        var ucsLastWriteTime = GetFileLastWriteTime(UcsFilePath);
        if (_ucsLastWriteTime == ucsLastWriteTime)
            return;

        try
        {
            using (var fs = new FileStream(UcsFilePath, FileMode.Open, FileAccess.Read))
            {
                var xdoc = XDocument.Load(fs).Element("Settings");
                _ucs = new UniversalCompilerSettings
                {
                    Compiler = (CompilerType)Enum.Parse(typeof (CompilerType), xdoc.Element("Compiler").Value),
                };
                _ucsLastWriteTime = ucsLastWriteTime;
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (Exception e)
        {
            Debug.LogWarning("LoadUniversalCompilerSettings:" + e);
        }
    }

    void SaveUniversalCompilerSettings()
    {
        try
        {
            XElement xel = new XElement("Settings");
            try
            {
                using (var fs = new FileStream(UcsFilePath, FileMode.Open, FileAccess.Read))
                {
                    xel = XDocument.Load(fs, LoadOptions.PreserveWhitespace).Element("Settings");
                }
            }
            catch (Exception)
            {
            }

            SetXmlElementValue(xel, "Compiler", _ucs.Compiler.ToString());

            xel.Save(UcsFilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveUniversalCompilerSettings:" + e);
        }
    }

    string GetUniversalCompilerVersion()
    {
        if (_ucVersion != null) {
            return _ucVersion;
        }

        var assemblyName = AssemblyName.GetAssemblyName("./Compiler/UniversalCompiler.exe");
        _ucVersion = assemblyName != null ? assemblyName.Version.ToString() : "";
        return _ucVersion;
    }

    void ShowUniversalCompilerClientLog()
    {
        Process.Start(Path.GetFullPath(UcLogFilePath));
    }

    LogElement[] GetUniversalCompilerLastBuildLogs()
    {
        if (!File.Exists(UcLogFilePath)) return _ucLastBuildLog;
        var ucLogLastWriteTime = GetFileLastWriteTime(UcLogFilePath);
        if (ucLogLastWriteTime == _ucLogLastWriteTime) return _ucLastBuildLog;
        _ucLogLastWriteTime = ucLogLastWriteTime;

        var result = new List<LogElement>();
        try
        {
            var lines = File.ReadAllLines(UcLogFilePath);

            var totalFound = 0;
            foreach (var line in lines.Reverse())
            {
                Debug.Log(line);
                if (line.StartsWith("compilation-info;", StringComparison.Ordinal)) {
                    var split = line.Split(';');
                    Debug.Log(line);
                    if (split.Length < 4) continue;

                    var target = split[1];
                    var elapsed = long.TryParse(split[2], out var res) ? res : 0;
                    var dateTime = DateTime.TryParse(
                        split[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt
                    ) ? dt : DateTime.UtcNow;
                    result.Add(new LogElement(target, elapsed, dateTime));

                    totalFound++;
                    if (totalFound >= 30) break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("GetUniversalCompilerLastBuildLogs:" + e);
        }

        _ucLastBuildLog = result.ToArray();

        return _ucLastBuildLog;
    }

    void OnGUI_IncrementalCompilerSettings()
    {
        GUILayout.Label("Incremental Compiler Settings", EditorStyles.boldLabel);

        LoadIncrementalCompilerSettings();
        IncrementalCompilerSettings ics;
        ics.PrebuiltOutputReuse = (PrebuiltOutputReuseType)EditorGUILayout.EnumPopup("PrebuiltOutputReuse:", _ics.PrebuiltOutputReuse);
        if (ics.Equals(_ics) == false)
        {
            _ics = ics;
            SaveIncrementalCompilerSettings();
        }
    }

    void LoadIncrementalCompilerSettings()
    {
        var icsLastWriteTime = GetFileLastWriteTime(IcsFilePath);
        if (icsLastWriteTime == _icsLastWriteTime)
            return;

        try
        {
            using (var fs = new FileStream(IcsFilePath, FileMode.Open, FileAccess.Read))
            {
                var xdoc = XDocument.Load(fs).Element("Settings");
                _ics = new IncrementalCompilerSettings
                {
                    PrebuiltOutputReuse = (PrebuiltOutputReuseType)
                        Enum.Parse(typeof(PrebuiltOutputReuseType), xdoc.Element("PrebuiltOutputReuse").Value),
                };
                _icsLastWriteTime = icsLastWriteTime;
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (Exception e)
        {
            Debug.LogWarning("LoadIncrementalCompilerSettings:" + e);
        }
    }

    void SaveIncrementalCompilerSettings()
    {
        try
        {
            XElement xel = new XElement("Settings");
            try
            {
                using (var fs = new FileStream(IcsFilePath, FileMode.Open, FileAccess.Read))
                {
                    xel = XDocument.Load(fs, LoadOptions.PreserveWhitespace).Element("Settings");
                }
            }
            catch (Exception)
            {
            }

            SetXmlElementValue(xel, "PrebuiltOutputReuse", _ics.PrebuiltOutputReuse.ToString());

            xel.Save(IcsFilePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveIncrementalCompilerSettings:" + e);
        }
    }

    void OnGUI_IncrementalCompilerStatus()
    {
        GUILayout.Label("Incremental Compiler Status", EditorStyles.boldLabel);

        EditorGUILayout.TextField("Version", GetIncrementalCompilerVersion());

        var icsProcess = GetIncrementalCompilerProcess();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Server");
        if (icsProcess != null)
        {
            GUILayout.TextField("Running");
            if (GUILayout.Button("Kill"))
                icsProcess.Kill();
        }
        else
        {
            GUILayout.TextField("Stopped");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Log");
        if (GUILayout.Button("Client"))
            ShowIncrementalCompilerClientLog();
        if (GUILayout.Button("Server"))
            ShowIncrementalCompilerServerLog();
        EditorGUILayout.EndHorizontal();
    }

    string GetIncrementalCompilerVersion()
    {
        var assemblyName = AssemblyName.GetAssemblyName("./Compiler/IncrementalCompiler.exe");
        return assemblyName != null ? assemblyName.Version.ToString() : "";
    }

    Process GetIncrementalCompilerProcess()
    {
        if (_icProcess != null && _icProcess.HasExited == false)
            return _icProcess;

        _icProcess = null;
        try
        {
            var processes = Process.GetProcessesByName("IncrementalCompiler");
            var dir = Directory.GetCurrentDirectory();
            foreach (var process in processes)
            {
                if (process.MainModule.FileName.StartsWith(dir))
                {
                    _icProcess = process;
                    return _icProcess;
                }
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    void ShowIncrementalCompilerClientLog()
    {
        Process.Start(Path.GetFullPath(@"./Compiler/Temp/IncrementalCompiler.log"));
    }

    void ShowIncrementalCompilerServerLog()
    {
        Process.Start(Path.GetFullPath(@"./Compiler/Temp/IncrementalCompiler-Server.log"));
    }

    // workaround for Xelement.SetElementValue bug at Unity3D
    // http://stackoverflow.com/questions/26429930/xelement-setelementvalue-overwrites-elements
    void SetXmlElementValue(XElement xel, XName name, string value)
    {
        var element = xel.Element(name);
        if (element != null)
            element.Value = value;
        else
            xel.Add(new XElement(name, value));
    }

    DateTime GetFileLastWriteTime(string path)
    {
        try
        {
            var fi = new FileInfo(IcsFilePath);
            return fi.LastWriteTimeUtc;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }
}
